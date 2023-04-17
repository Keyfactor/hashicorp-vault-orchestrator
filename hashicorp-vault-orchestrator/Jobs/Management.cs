// Copyright 2022 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    public class Management : JobBase, IManagementJobExtension
    {
        readonly ILogger logger = LogHandler.GetClassLogger<Management>();

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            InitializeStore(config);

            JobResult complete = new JobResult()
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage = "Invalid Management Operation"
            };

            switch (config.OperationType)
            {
                case CertStoreOperationType.Add:
                    logger.LogDebug($"Begin Management > Add...");
                    complete = PerformAddition(config.JobCertificate.Alias, config.JobCertificate.PrivateKeyPassword, config.JobCertificate.Contents, config.JobHistoryId);
                    break;
                case CertStoreOperationType.Remove:
                    logger.LogDebug($"Begin Management > Remove...");
                    complete = PerformRemoval(config.JobCertificate.Alias, config.JobHistoryId);
                    break;
            }

            return complete;
        }

        protected virtual JobResult PerformAddition(string alias, string pfxPassword, string entryContents, long jobHistoryId)
        {
            var complete = new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = jobHistoryId };

            if (!string.IsNullOrWhiteSpace(pfxPassword)) // This is a PFX Entry
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    complete.FailureMessage = "You must supply an alias for the certificate.";
                    return complete;
                }

                try
                {
                    // uploadCollection is either not null or an exception was thrown.
                    var cert = VaultClient.PutCertificate(alias, entryContents, pfxPassword);
                    complete.Result = OrchestratorJobStatusJobResult.Success;
                }
                catch (Exception ex)
                {
                    if (ex.GetType() == typeof(NotSupportedException))
                    {
                        logger.LogError("Attempt to Add Certificate on unsupported Secrets Engine backend.");
                        complete.FailureMessage = $"{SecretsEngine} does not support adding certificates via the Orchestrator.";
                    }
                    else
                    {
                        complete.FailureMessage = $"An error occured while adding {alias} to {ExtensionName}: " + ex.Message;

                        if (ex.InnerException != null)
                            complete.FailureMessage += " - " + ex.InnerException.Message;
                    }
                }
            }

            else  // Non-PFX
            {
                complete.FailureMessage = "Certificate to add must be in a .PFX file format.";
            }

            return complete;
        }

        protected virtual JobResult PerformRemoval(string alias, long jobHistoryId)
        {
            JobResult complete = new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = jobHistoryId };

            if (string.IsNullOrWhiteSpace(alias))
            {
                complete.FailureMessage = "You must supply an alias for the certificate.";
                return complete;
            }

            try
            {
                var success = VaultClient.DeleteCertificate(alias).Result;

                if (!success)
                {
                    complete.FailureMessage = $"Error removing {alias} from Vault";
                }
                else
                {
                    complete.Result = OrchestratorJobStatusJobResult.Success;
                }
            }

            catch (Exception ex)
            {
                if (ex.GetType() == typeof(NotSupportedException))
                {
                    logger.LogError("Attempt to Delete Certificate on unsupported Secrets Engine backend.");
                    complete.FailureMessage = $"{SecretsEngine} does not support removing certificates via the Orchestrator.";
                }
                else
                {
                    logger.LogError("Error deleting cert from Vault", ex);
                    complete.FailureMessage = $"An error occured while removing {alias} from {ExtensionName}: " + ex.Message;
                }
            }
            return complete;
        }
    }
}
