﻿#nullable enable

using CopyGitLink.Def;
using CopyGitLink.Def.Models;
using Microsoft;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CopyGitLink.Shared.Core.GitOnlineServices
{
    [Export(typeof(IGitOnlineService))]
    internal sealed class AzureDevOps : IGitOnlineService
    {
        private const string Organization = "Organization";
        private const string Project = "Project";
        private const string Repository = "Repository";
        private const string RepositoryUrl = "RepositoryUrl";
        private const string OrganizationUrl = "OrganizationUrl";

        private readonly IGitCommandService _gitCommandService;

        [ImportingConstructor]
        internal AzureDevOps(IGitCommandService gitCommandService)
        {
            _gitCommandService = gitCommandService;
        }

        public Task<bool> TryDetectRepositoryInformationAsync(
            string repositoryFolder,
            string repositoryUri,
            CancellationToken cancellationToken,
            out RepositoryInfo? repositoryInfo)
        {
            if (!TryParseGitUri(repositoryUri, out IDictionary<string, string>? properties)
                || properties == null)
            {
                repositoryInfo = null;
                return Task.FromResult(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            repositoryInfo = new RepositoryInfo(repositoryUri, properties, this);
            return Task.FromResult(true);
        }

        public async Task<string> GenerateLinkAsync(
            string repositoryFolder,
            RepositoryInfo repositoryInfo,
            string filePath,
            long? startLineNumber = null,
            long? startColumnNumber = null,
            long? endLineNumber = null,
            long? endColumnNumber = null)
        {
            Requires.NotNullOrEmpty(repositoryFolder, nameof(repositoryFolder));
            Requires.NotNull(repositoryInfo, nameof(repositoryInfo));
            Requires.NotNullOrEmpty(filePath, nameof(filePath));

            string branchName
                = Uri.EscapeDataString(
                    await _gitCommandService.GetBestRemoteGitBranchAsync(repositoryFolder)
                    .ConfigureAwait(false));

            var relativePath
                = Uri.EscapeDataString(
                    filePath.Substring(repositoryFolder.Length))
                .Replace("%5C", "/");

            var repositoryUrl = repositoryInfo.Properties[RepositoryUrl];

            Requires.NotNullOrEmpty(repositoryUrl, nameof(repositoryUrl));
            Requires.NotNullOrEmpty(branchName, nameof(branchName));

            // Link to a file without line to select.
            string url = $"{repositoryUrl}?path={relativePath}&version=GB{branchName}&lineStyle=plain";

            if (startLineNumber.HasValue)
            {
                url += $"&line={startLineNumber + 1}";
            }

            if (endLineNumber.HasValue)
            {
                url += $"&lineEnd={endLineNumber + 1}";
            }

            if (startColumnNumber.HasValue)
            {
                url += $"&lineStartColumn={startColumnNumber + 1}";
            }

            if (endColumnNumber.HasValue)
            {
                url += $"&lineEndColumn={endColumnNumber + 1}";
            }

            return url;
        }

        /// <summary>
        /// Parse an Azure DevOps / VSO Url and returns information detected from this Url.
        /// </summary>
        /// <param name="repositoryUriString">A string that suppose to be an Azyre DevOps / VSO Url.</param>
        /// <param name="properties">Returns a dictionary containing info about the <paramref name="repositoryUriString"/>.</param>
        /// <returns>Returns <c>False</c> if <paramref name="repositoryUriString"/> is not a valid Azure DevOps Url.</returns>
        private bool TryParseGitUri(string repositoryUriString, out IDictionary<string, string>? properties)
        {
            if (string.IsNullOrWhiteSpace(repositoryUriString))
            {
                properties = null;
                return false;
            }

            Uri repositoryUri;
            try
            {
                repositoryUri = new Uri(repositoryUriString);
            }
            catch
            {
                // The string isn't a URI.
                properties = null;
                return false;
            }

            // Detect the scheme. We only accept http and https.
            if (!string.Equals(repositoryUri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(repositoryUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                properties = null;
                return false;
            }

            properties = new Dictionary<string, string>();

            // Detect if the host corresponds to Azure DevOps.
            // It can be
            // * dev.azure.com
            // * visualstudio.com
            // * or a private server url such like https://tfs.contoso.com:8080/tfs/Project.
            // see https://docs.microsoft.com/en-us/azure/devops/server/admin/websitesettings?view=azure-devops
            if (string.Equals(repositoryUri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
                && repositoryUri.Segments.Length >= 6)
            {
                properties[Organization] = repositoryUri.Segments[1].TrimEnd('/');
                properties[Project] = repositoryUri.Segments[3].TrimEnd('/');
                properties[Repository] = repositoryUri.Segments[5].TrimEnd('/');
                properties[OrganizationUrl] = $"{repositoryUri.Scheme}://{repositoryUri.Host}/{properties[Organization]}/";
                properties[RepositoryUrl] = $"{properties[OrganizationUrl]}{properties[Project]}/_git/{properties[Repository]}/";
                return true;
            }
            else if (repositoryUri.Host.Count(c => c == '.') == 2
                     && repositoryUri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
                     && repositoryUri.Segments.Length >= 5)
            {
                properties[Organization] = repositoryUri.Host.Split('.')[0];
                properties[Project] = repositoryUri.Segments[2].TrimEnd('/');
                properties[Repository] = repositoryUri.Segments[4].TrimEnd('/');
                properties[OrganizationUrl] = $"{repositoryUri.Scheme}://{repositoryUri.Host}/";
                properties[RepositoryUrl] = $"{properties[OrganizationUrl]}{properties[Project]}/_git/{properties[Repository]}/";
                return true;
            }
            else if (repositoryUri.Port == 8080
                     && repositoryUri.Segments.Length >= 5
                     && string.Equals(repositoryUri.Segments[1], "tfs/", StringComparison.Ordinal))
            {
                properties[Organization] = repositoryUri.Host;
                properties[Project] = repositoryUri.Segments[2].TrimEnd('/');
                properties[Repository] = repositoryUri.Segments[4].TrimEnd('/');
                properties[OrganizationUrl] = $"{repositoryUri.Scheme}://{repositoryUri.Authority}/tfs/";
                properties[RepositoryUrl] = $"{properties[OrganizationUrl]}{properties[Project]}/_git/{properties[Repository]}/";
                return true;
            }

            properties = null;
            return false;
        }

    }
}
