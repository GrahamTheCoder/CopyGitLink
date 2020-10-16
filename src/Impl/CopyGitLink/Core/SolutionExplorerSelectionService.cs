﻿#nullable enable

using CopyGitLink.Def;
using Microsoft;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace CopyGitLink.Core
{
    [Export(typeof(ISolutionExplorerSelectionService))]
    internal sealed class SolutionExplorerSelectionService : ISolutionExplorerSelectionService, IVsSelectionEvents, IDisposable
    {
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly SVsServiceProvider _serviceProvider;
        private readonly IRepositoryService _repositoryService;

        private IVsMonitorSelection? _monitorSelection;
        private uint _uiShellCookie = VSConstants.VSCOOKIE_NIL;

        public string CurrentSelectedItemFullPath { get; private set; } = string.Empty;

        [ImportingConstructor]
        internal SolutionExplorerSelectionService(
            SVsServiceProvider serviceProvider,
            JoinableTaskContext joinableTaskContext,
            IRepositoryService repositoryService)
        {
            _serviceProvider = serviceProvider;
            _joinableTaskFactory = joinableTaskContext.Factory;
            _repositoryService = repositoryService;

            StartListeningToSelectionEventsAsync().Forget();
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StopListeningToSelectionEvents();
        }

        public int OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld, ISelectionContainer pSCOld, IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew, ISelectionContainer pSCNew)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string fullPath = GetFilePath(pHierNew, itemidNew);

            CurrentSelectedItemFullPath = fullPath;

            if (!string.IsNullOrEmpty(fullPath))
            {
                _repositoryService.QueueRepositoryDiscovery(fullPath);
            }

            return VSConstants.S_OK;
        }

        public int OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
        {
            return VSConstants.S_OK;
        }

        public int OnCmdUIContextChanged(uint dwCmdUICookie, int fActive)
        {
            return VSConstants.S_OK;
        }

        private async Task StartListeningToSelectionEventsAsync()
        {
            if (_uiShellCookie == VSConstants.VSCOOKIE_NIL)
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();

                _monitorSelection = _serviceProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
                Assumes.Present(_monitorSelection);

                if (_monitorSelection != null
                    && ErrorHandler.Failed(_monitorSelection.AdviseSelectionEvents(this, out _uiShellCookie)))
                {
                    Debug.Fail("Unable to start listening to selection events;");
                }
            }
        }

        private void StopListeningToSelectionEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_uiShellCookie != VSConstants.VSCOOKIE_NIL)
            {
                if (_monitorSelection != null
                    && ErrorHandler.Failed(_monitorSelection.UnadviseSelectionEvents(_uiShellCookie)))
                {
                    Debug.Fail("Unable to stop listening to selection events;");
                }

                _uiShellCookie = VSConstants.VSCOOKIE_NIL;
            }
        }

        /// <summary>
        /// Gets the full path of a hierarchy item.
        /// </summary>
        private string GetFilePath(IVsHierarchy hierarchy, uint itemId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // GetMkDocument and GetCanonicalName don't work on the solution/root node.
            if (hierarchy == null
                || HierarchyUtilities.IsSolutionNode(hierarchy, itemId))
            {
                return RetrieveCurrentSolutionPath();
            }

            int hr;
            string? file;

            // We prefer IVsProject, but it's not available in all projects.
            if (hierarchy is IVsProject project)
            {
                hr = project.GetMkDocument(itemId, out file);
            }
            else
            {
                hr = hierarchy.GetCanonicalName(itemId, out file);
            }

            if (ErrorHandler.Failed(hr) || file == null)
            {
                file = string.Empty;
            }

            return file;
        }

        private string RetrieveCurrentSolutionPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Get opened solution/folder path.
            var vsSolution = (IVsSolution)_serviceProvider.GetService(typeof(SVsSolution));
            if (vsSolution != null
                && ErrorHandler.Succeeded(vsSolution.GetSolutionInfo(out string solutionOrFolderDirectory, out _, out _)))
            {
                return solutionOrFolderDirectory;
            }

            return string.Empty;
        }
    }
}
