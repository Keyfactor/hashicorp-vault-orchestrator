// Copyright 2022 Keyfactor
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
        ILogger logger = LogHandler.GetClassLogger<Discovery>();

        public JobResult ProcessJob(DiscoveryJobConfiguration config, SubmitDiscoveryUpdate submitDiscoveryUpdate)
        {
            InitializeStore(config);

            List<string> vaults = new List<string>();

            try
            {
                vaults = VaultClient.GetVaults().Result.ToList();
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

                    result.FailureMessage = $"{SecretsEngine} does not support Discovery jobs.";
                }
                else
                {
                    result.FailureMessage = ex.Message;
                }
                return result;
            }

            submitDiscoveryUpdate.DynamicInvoke(vaults);

            return new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Success,
                JobHistoryId = config.JobHistoryId,
                FailureMessage = string.Empty
            };
        }
    }
}
