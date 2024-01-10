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
    public class Inventory : JobBase, IInventoryJobExtension
    {
        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventoryUpdate)
        {
            IEnumerable<CurrentInventoryItem> certs = null;
            var warnings = new List<string>();
            Initialize(config);

            try
            {
                (certs, warnings) = VaultClient.GetCertificates().Result;
                var success = submitInventoryUpdate.Invoke(certs.ToList());

                if (!success)
                {
                    logger.LogTrace("failure submitting results to the platform.");
                }

                return new JobResult
                {
                    Result = success ? OrchestratorJobStatusJobResult.Success : OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = success ? string.Empty : "Error executing SubmitInventoryUpdate"
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"error performing inventory: {ex.Message}");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ex.Message
                };
            }
        }
    }
}
