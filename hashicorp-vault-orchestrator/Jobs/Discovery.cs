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

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    public class Discovery : JobBase, IDiscoveryJobExtension
    {
        public JobResult ProcessJob(DiscoveryJobConfiguration config, SubmitDiscoveryUpdate submitDiscoveryUpdate)
        {
            var jobStatus = OrchestratorJobStatusJobResult.Failure;
            var failureMessage = string.Empty;

            Initialize(config);

            List<string> vaults;
            List<string> warnings;

            try
            {
                (vaults, warnings) = VaultClient.GetVaults(StorePath).Result;

                if (vaults.Count() > 0) jobStatus = OrchestratorJobStatusJobResult.Success;

                // if vaults were discovered, but there are warnings, the job status is "warning".
                if (vaults.Count() > 0 && warnings.Count() > 0) {
                    jobStatus = OrchestratorJobStatusJobResult.Warning;
                    failureMessage = $"Discovered {vaults.Count()} vaults, but encountered {warnings.Count()} errors during discovery:\n{string.Join("\n", warnings)}";
                }
                // if no vaults were discovered, but there are warnings, the job status is "failure".

                if (vaults.Count() == 0 && warnings?.Count() > 0) {
                    failureMessage = $"{warnings.Count()} errors during discovery job:\n{string.Join("\n", warnings)}"; 
                }

                if (!warnings.Any()) {
                    failureMessage = $"Completed discovery job successfully.  Discovered {vaults.Count()} vaults."; 
                }
                
                submitDiscoveryUpdate.DynamicInvoke(vaults);

                return new JobResult
                {
                    Result = jobStatus,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
            catch (Exception ex)
            {
                var result = new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId
                };

                if (ex.GetType() == typeof(NotSupportedException))
                {
                    logger.LogError("Attempt to perform discovery on unsupported Secrets Engine backend.");
                    result.FailureMessage = $"{_storeType} does not support Discovery jobs.";
                }
                else
                {
                    logger.LogError(ex, $"Error running discovery job.\nException type:{ex.GetType().Name}\nException message: {ex.Message}\nInner exception message: {ex.InnerException?.Message}");
                    result.FailureMessage = $"Error running discovery job. {ex.Message}";
                }
                return result;
            }
        }
    }
}
