
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Pipelines.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi; // for VssServiceResponseException
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdoAgentDemo
{
    /// <summary>
    /// Custom Tools facade for Azure DevOps. Static methods return JSON strings,
    /// designed to be invoked by Azure AI Agents "Custom Tools".
    /// </summary>
    public static class DevOpsTools
    {
        // JSON options (camelCase + ignore nulls)
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        // ---------------------------
        // Pipelines (Builds)
        // ---------------------------

        /// <summary>
        /// Get build (pipeline) definitions in a project.
        /// </summary>
        public static string GetBuildDefinitions(
            string? nameFilter = null,
            string? organizationUrl = null,
            string? project = null,
            string? personalAccessToken = null)
        {
            return Run("GetBuildDefinitions", organizationUrl, project, personalAccessToken, ado =>
            {
                var defs = ado.GetBuildDefinitionsAsync(nameFilter).GetAwaiter().GetResult();
                return Success(new { count = defs.Count, items = defs });
            });
        }

        /// <summary>
        /// Get latest builds. Optional filter by definitionId and result.
        /// resultFilter (case-insensitive): Succeeded, Failed, PartiallySucceeded, Canceled.
        /// </summary>
        public static string GetLatestBuilds(
            int top = 20,
            int? definitionId = null,
            string? resultFilter = null,
            string? organizationUrl = null,
            string? project = null,
            string? personalAccessToken = null)
        {
            return Run("GetLatestBuilds", organizationUrl, project, personalAccessToken, ado =>
            {
                BuildResult? parsed = ParseBuildResult(resultFilter);
                var builds = ado.GetLatestBuildsAsync(top, definitionId, parsed).GetAwaiter().GetResult();
                return Success(new { count = builds.Count, items = builds });
            });
        }

        /// <summary>
        /// Get a single build by id.
        /// </summary>
        public static string GetBuild(
            int buildId,
            string? organizationUrl = null,
            string? project = null,
            string? personalAccessToken = null)
        {
            return Run("GetBuild", organizationUrl, project, personalAccessToken, ado =>
            {
                var build = ado.GetBuildAsync(buildId).GetAwaiter().GetResult();
                if (build is null) return NotFound($"Build {buildId} not found.");
                return Success(new { item = build });
            });
        }

        /// <summary>
        /// Get the most recent completed build's status for a definition.
        /// </summary>
        public static string GetLatestBuildStatus(
            int definitionId,
            string? organizationUrl = null,
            string? project = null,
            string? personalAccessToken = null)
        {
            return Run("GetLatestBuildStatus", organizationUrl, project, personalAccessToken, ado =>
            {
                var status = ado.GetLatestBuildStatusAsync(definitionId).GetAwaiter().GetResult();
                if (status is null) return NotFound($"No completed builds for definition {definitionId}.");
                return Success(new { item = status });
            });
        }

        // ---------------------------
        // Releases & Deployments (Classic)
        // ---------------------------

        /// <summary>
        /// Get releases (classic Release pipelines).
        /// </summary>
        public static string GetReleases(
            int top = 20,
            int? definitionId = null,
            string? organizationUrl = null,
            string? project = null,
            string? personalAccessToken = null)
        {
            return Run("GetReleases", organizationUrl, project, personalAccessToken, ado =>
            {
                var releases = ado.GetReleasesAsync(top, definitionId).GetAwaiter().GetResult();
                return Success(new { count = releases.Count, items = releases });
            });
        }

        /// <summary>
        /// Get deployments (classic release). Optional filter by definition/environment/status.
        /// statusFilter (case-insensitive): NotDeployed, InProgress, Succeeded, PartialSucceeded, Failed, All.
        /// </summary>
        public static string GetDeployments(
            int? definitionId = null,
            int? environmentId = null,
            string? statusFilter = null,
            int top = 50,
            string? organizationUrl = null,
            string? project = null,
            string? personalAccessToken = null)
        {
            return Run("GetDeployments", organizationUrl, project, personalAccessToken, ado =>
            {
                DeploymentStatus? parsed = ParseDeploymentStatus(statusFilter);
                var deps = ado.GetDeploymentsAsync(definitionId, environmentId, parsed, top).GetAwaiter().GetResult();
                return Success(new { count = deps.Count, items = deps });
            });
        }

        // ===========================
        // Internals / helpers
        // ===========================

        private static string Run(
            string op,
            string? organizationUrl,
            string? project,
            string? personalAccessToken,
            Func<AzureDevOpsClient, string> action)
        {
            var cfg = ResolveConfig(organizationUrl, project, personalAccessToken);
            AzureDevOpsClient? ado = null;

            try
            {
                ado = new AzureDevOpsClient(cfg.OrgUrl, cfg.Project, cfg.PAT);
                var resultJson = action(ado);
                return resultJson;
            }
            catch (VssServiceResponseException ex)
            {
                return Error(op, "AzureDevOpsServiceError", ex.Message, ex.ErrorCode.ToString());
            }
            catch (ArgumentNullException ex)
            {
                return Error(op, "InvalidArgument", ex.Message);
            }
            catch (Exception ex)
            {
                return Error(op, "UnhandledError", ex.Message);
            }
            finally
            {
                if (ado is not null)
                {
                    try { ado.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* swallow */ }
                }
            }
        }

        private static (string OrgUrl, string Project, string PAT) ResolveConfig(
            string? orgUrl, string? project, string? pat)
        {
            var resolvedOrg = !string.IsNullOrWhiteSpace(orgUrl)
                ? orgUrl
                : Environment.GetEnvironmentVariable("ADO_ORG_URL");

            var resolvedProject = !string.IsNullOrWhiteSpace(project)
                ? project
                : Environment.GetEnvironmentVariable("ADO_PROJECT");

            var resolvedPat = !string.IsNullOrWhiteSpace(pat)
                ? pat
                : Environment.GetEnvironmentVariable("ADO_PAT");

            if (string.IsNullOrWhiteSpace(resolvedOrg))
                throw new ArgumentNullException(nameof(orgUrl), "organizationUrl is required (or set ADO_ORG_URL).");
            if (string.IsNullOrWhiteSpace(resolvedProject))
                throw new ArgumentNullException(nameof(project), "project is required (or set ADO_PROJECT).");
            if (string.IsNullOrWhiteSpace(resolvedPat))
                throw new ArgumentNullException(nameof(pat), "personalAccessToken is required (or set ADO_PAT).");

            return (resolvedOrg, resolvedProject!, resolvedPat);
        }

        private static BuildResult? ParseBuildResult(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return Enum.TryParse<BuildResult>(s, ignoreCase: true, out var val) ? val : null;
        }

        private static DeploymentStatus? ParseDeploymentStatus(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return Enum.TryParse<DeploymentStatus>(s, ignoreCase: true, out var val) ? val : null;
        }

        private static string Success(object payload) =>
            JsonSerializer.Serialize(new { ok = true, data = payload }, Json);

        private static string NotFound(string message) =>
            JsonSerializer.Serialize(new { ok = false, error = new { type = "NotFound", message } }, Json);

        private static string Error(string op, string type, string message, string? code = null) =>
            JsonSerializer.Serialize(new
            {
                ok = false,
                error = new { operation = op, type, code, message }
            }, Json);
    }
}


