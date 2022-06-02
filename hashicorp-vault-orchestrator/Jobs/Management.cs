// Copyright 2022 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Threading.Tasks;
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
                case CertStoreOperationType.Create:
                    logger.LogDebug($"Begin Management > Create...");
                    complete = PerformCreateVault(config.JobHistoryId).Result;
                    break;
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

        protected async Task<JobResult> PerformCreateVault(long jobHistoryId)
        {
            var jobResult = new JobResult() { JobHistoryId = jobHistoryId, Result = OrchestratorJobStatusJobResult.Failure };
            bool createVaultResult;
            try
            {
                createVaultResult = await VaultClient.CreateStore(StorePath, MountPoint);
            }
            catch (Exception ex)
            {
                jobResult.FailureMessage = ex.Message;
                return jobResult;
            }

            if (createVaultResult)
            {
                jobResult.Result = OrchestratorJobStatusJobResult.Success;
            }
            else
            {
                jobResult.FailureMessage = "The creation of the Azure Key Vault failed for an unknown reason. Check your job parameters and ensure permissions are correct.";
            }

            return jobResult;
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
                    var cert = VaultClient.PutCertificate(alias, entryContents, pfxPassword, StorePath, MountPoint);
                    complete.Result = OrchestratorJobStatusJobResult.Success;
                }
                catch (Exception ex)
                {
                    complete.FailureMessage = $"An error occured while adding {alias} to {ExtensionName}: " + ex.Message;

                    if (ex.InnerException != null)
                        complete.FailureMessage += " - " + ex.InnerException.Message;
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
                var success = VaultClient.DeleteCertificate(alias, StorePath, MountPoint).Result;

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
                logger.LogError("Error deleting cert from Vault", ex);
                complete.FailureMessage = $"An error occured while removing {alias} from {ExtensionName}: " + ex.Message;
            }
            return complete;
        }
    }
}
