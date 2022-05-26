using System;
using System.Collections.Generic;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    [Job(JobTypes.INVENTORY)]
    public class Inventory : JobBase, IInventoryJobExtension
    {
        ILogger logger = LogHandler.GetClassLogger<Inventory>();

        public Inventory()
        {
            VaultClient = new HcvClient(VaultToken, VaultServerUrl);
        }


        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventoryUpdate)
        {
            InitializeStore(config);

            IEnumerable<CurrentInventoryItem> certs = null;
            try
            {
                certs = VaultClient.GetCertificates(StorePath, MountPoint).Result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ex.Message
                };
            }

            submitInventoryUpdate.DynamicInvoke(certs);

            return new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Success,
                JobHistoryId = config.JobHistoryId,
                FailureMessage = string.Empty
            };
        }
    }
}
