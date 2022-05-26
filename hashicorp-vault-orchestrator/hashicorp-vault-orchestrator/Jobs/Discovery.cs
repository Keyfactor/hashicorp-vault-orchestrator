using System;
using System.Collections.Generic;
using System.Linq;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    [Job(JobTypes.DISCOVERY)]
    public class Discovery : JobBase, IInventoryJobExtension
    {
        ILogger logger = LogHandler.GetClassLogger<Inventory>();

        public Discovery()
        {
            VaultClient = new HcvClient(VaultToken, VaultServerUrl);
        }

        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventoryUpdate)
        {
            InitializeStore(config);

            List<string> vaults = new List<string>();

            try
            {
                vaults = VaultClient.GetVaults(StorePath, MountPoint).Result.ToList();

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

            submitInventoryUpdate.DynamicInvoke(vaults);

            return new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Success,
                JobHistoryId = config.JobHistoryId,
                FailureMessage = string.Empty
            };
        }
    }
}
