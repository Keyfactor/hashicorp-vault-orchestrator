using System;
using System.Collections.Generic;
using System.Text;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs
{
    [Job(JobTypes.MANAGEMENT)]
    public class Management
    {
        readonly ILogger logger = LogHandler.GetClassLogger<Management>();
    }
}
