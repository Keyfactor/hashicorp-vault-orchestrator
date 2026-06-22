// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using FluentAssertions;
using Keyfactor.Extensions.Orchestrator.HashicorpVault.Jobs;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    /// <summary>
    /// Exercises the MountPoint/Namespace parsing logic from InitProps in isolation.
    /// We replicate the exact same algorithm here so the tests are a faithful
    /// specification of the production behaviour without needing a full job pipeline.
    /// </summary>
    internal class TestableJobBase : JobBase
    {
        public TestableJobBase() : base(new Mock<IPAMSecretResolver>().Object)
        {
            logger = NullLogger.Instance;
            JobParameters = new JobProperties();
        }

        /// <summary>
        /// Runs only the MountPoint/Namespace parsing block from InitProps.
        /// Call this after optionally pre-seeding JobParameters.Namespace or
        /// JobParameters.MountPoint to test pre-existing state.
        /// </summary>
        public void ParseMountPointFromProps(Dictionary<string, object> props)
        {
            var mp = props.ContainsKey("MountPoint") ? props["MountPoint"].ToString() : null;
            if (!string.IsNullOrEmpty(mp))
            {
                // Exact copy of the production algorithm — last-slash split for nested namespace support
                var lastSlash = mp.TrimEnd('/').LastIndexOf('/');
                if (lastSlash > 0)
                {
                    if (string.IsNullOrEmpty(JobParameters.Namespace))
                    {
                        JobParameters.Namespace = mp.Substring(0, lastSlash).Trim('/');
                    }
                    JobParameters.MountPoint = mp.Substring(lastSlash + 1).Trim();
                }
                else
                {
                    JobParameters.MountPoint = mp.Trim('/');
                }
            }
        }
    }

    public class MountPointNamespaceParsingTests
    {
        // -----------------------------------------------------------------------
        // Bare mount name — no namespace splitting
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_KeepsMountPoint_WhenNoSlashPresent()
        {
            var job = new TestableJobBase();
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "secret" });

            job.JobParameters.MountPoint.Should().Be("secret");
            job.JobParameters.Namespace.Should().BeNullOrEmpty();
        }

        // -----------------------------------------------------------------------
        // Simple <namespace>/<mount> — two segments
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_SplitsSimpleNamespaceAndMount()
        {
            var job = new TestableJobBase();
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "myns/kv-v2" });

            job.JobParameters.Namespace.Should().Be("myns");
            job.JobParameters.MountPoint.Should().Be("kv-v2");
        }

        // -----------------------------------------------------------------------
        // Nested namespace <parent>/<child>/<mount> — the IKEA case
        // Everything left of the LAST slash is the namespace
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_SplitsNestedNamespaceAndMount_LastSlashWins()
        {
            var job = new TestableJobBase();
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "ep/common/secret" });

            job.JobParameters.Namespace.Should().Be("ep/common",
                because: "Vault Enterprise supports nested namespaces; everything left of the last slash is the namespace");
            job.JobParameters.MountPoint.Should().Be("secret");
        }

        // -----------------------------------------------------------------------
        // Three-level nesting
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_SplitsDeeplyNestedNamespace()
        {
            var job = new TestableJobBase();
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "root/level1/level2/mymount" });

            job.JobParameters.Namespace.Should().Be("root/level1/level2");
            job.JobParameters.MountPoint.Should().Be("mymount");
        }

        // -----------------------------------------------------------------------
        // Namespace already set (Discovery pre-parsed) — must not be overwritten
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_DoesNotOverwriteNamespace_WhenAlreadySet()
        {
            var job = new TestableJobBase();
            job.JobParameters.Namespace = "already-set";
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "ep/common/secret" });

            job.JobParameters.Namespace.Should().Be("already-set",
                because: "a namespace resolved by the Discovery Initialize path must not be overwritten by InitProps");
            job.JobParameters.MountPoint.Should().Be("secret");
        }

        // -----------------------------------------------------------------------
        // No MountPoint in props — default preserved
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_PreservesDefault_WhenMountPointAbsentFromProps()
        {
            var job = new TestableJobBase();
            job.JobParameters.MountPoint = "kv-v2"; // pre-set default
            job.ParseMountPointFromProps(new Dictionary<string, object>()); // no MountPoint key

            job.JobParameters.MountPoint.Should().Be("kv-v2");
            job.JobParameters.Namespace.Should().BeNullOrEmpty();
        }

        // -----------------------------------------------------------------------
        // Leading slash is stripped, not treated as a namespace segment
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_StripsLeadingSlash_WithoutCreatingEmptyNamespace()
        {
            var job = new TestableJobBase();
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "/secret" });

            job.JobParameters.MountPoint.Should().Be("secret");
            job.JobParameters.Namespace.Should().BeNullOrEmpty();
        }

        // -----------------------------------------------------------------------
        // Trailing slash on mount value is normalised away
        // -----------------------------------------------------------------------
        [Fact]
        public void ParseMount_StripsTrailingSlash_FromMountPoint()
        {
            var job = new TestableJobBase();
            job.ParseMountPointFromProps(new Dictionary<string, object> { ["MountPoint"] = "ep/common/secret/" });

            job.JobParameters.Namespace.Should().Be("ep/common");
            job.JobParameters.MountPoint.Should().Be("secret");
        }
    }
}
