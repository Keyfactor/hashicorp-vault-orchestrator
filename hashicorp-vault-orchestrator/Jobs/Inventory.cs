// Copyright 2023 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    public class Inventory : JobBase, IInventoryJobExtension
    {
        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventoryUpdate)
        {
            var failureMessage = "Error executing inventory";
            var resultStatus = OrchestratorJobStatusJobResult.Failure;
            IEnumerable<CurrentInventoryItem> certs = null;
            List<string> warnings;

            Initialize(config);

            try
            {
                (certs, warnings) = VaultClient.GetCertificates().Result;
                var success = submitInventoryUpdate.Invoke(certs?.ToList() ?? new List<CurrentInventoryItem>());
                
                if (success) {
                    resultStatus = OrchestratorJobStatusJobResult.Success;
                    failureMessage = $"Found {certs?.Count() ?? 0} valid certificates.";
                }

                if (success && warnings?.Count() > 0) {
                    resultStatus = OrchestratorJobStatusJobResult.Warning;
                    failureMessage = $"Found {certs?.Count() ?? 0} valid certificates, and {warnings?.Count()} entries that were unable to be included.\n{ string.Join("\n", warnings)}";
                }

                if (certs?.Count() == 0 && warnings?.Count() > 0)
                {
                    failureMessage = $"{warnings?.Count()} errors during inventory job:\n{string.Join("\n", warnings)}";
                    resultStatus = OrchestratorJobStatusJobResult.Failure;
                }

                if (failureMessage.Length > 3500) { failureMessage = failureMessage.Substring(0, 2000) + "\n -- contents have been truncated -- \n Please check the orchestrator logs for the remaining errors."; }

                if (!success)
                {
                    logger.LogTrace("failure submitting results to the platform.");
                }

                return new JobResult
                {
                    Result = resultStatus,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error performing inventory: {ex.Message}");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = $"Error performing inventory: {ex.Message}"
                };
            }
        }
    }
}
