using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpLab.Container.Manager.Internal {
    public class ContainerAllocationWorker : BackgroundService {
        private readonly ContainerPool _containerPool;
        private readonly DockerClient _dockerClient;
        private readonly ContainerNameFormat _containerNameFormat;
        private readonly ExecutionProcessor _warmupExecutionProcessor;
        private readonly ContainerCleanupWorker _containerCleanup;
        private readonly ILogger<ContainerAllocationWorker> _logger;

        private readonly byte[] _warmupAssemblyBytes;

        public ContainerAllocationWorker(
            ContainerPool containerPool,
            DockerClient dockerClient,
            ContainerNameFormat containerNameFormat,
            ExecutionProcessor warmupExecutionProcessor,
            ContainerCleanupWorker containerCleanup,
            ILogger<ContainerAllocationWorker> logger
        ) {
            _containerPool = containerPool;
            _dockerClient = dockerClient;
            _containerNameFormat = containerNameFormat;
            _warmupExecutionProcessor = warmupExecutionProcessor;
            _containerCleanup = containerCleanup;
            _logger = logger;

            _warmupAssemblyBytes = File.ReadAllBytes(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SharpLab.Container.Warmup.dll")
            );
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger.LogInformation("ContainerAllocationWorker starting");
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await _containerPool.PreallocatedContainersWriter.WaitToWriteAsync(stoppingToken);
                    var container = await CreateAndStartContainerAsync(stoppingToken);
                    await _containerPool.PreallocatedContainersWriter.WriteAsync(container, stoppingToken);
                    _containerPool.LastContainerPreallocationException = null;
                }
                catch (Exception ex) {
                    _containerPool.LastContainerPreallocationException = ex;
                    _logger.LogError(ex, "Failed to pre-allocate next container, retryng in 1 minute.");
                    try {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                    catch (TaskCanceledException cancelEx) when (cancelEx.CancellationToken == stoppingToken) {
                    }
                }
            }
            _containerPool.PreallocatedContainersWriter.Complete();
        }

        private async Task<ActiveContainer> CreateAndStartContainerAsync(CancellationToken cancellationToken) {
            var containerName = _containerNameFormat.GeneratePreallocatedName();
            _logger.LogDebug($"Allocating container {containerName}");
            var containerId = (await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters {
                Name = containerName,
                Image = "mcr.microsoft.com/dotnet/runtime:5.0",
                Cmd = new[] { @"c:\\app\SharpLab.Container.exe" },

                AttachStdout = true,
                AttachStdin = true,
                OpenStdin = true,
                StdinOnce = true,

                NetworkDisabled = true,
                HostConfig = new HostConfig {
                    Isolation = "process",
                    Mounts = new[] {
                        new Mount {
                            Source = AppDomain.CurrentDomain.BaseDirectory,
                            Target = @"c:\app",
                            Type = "bind",
                            ReadOnly = true
                        }
                    },
                    Memory = 100 * 1024 * 1024,
                    CPUQuota = 50000,

                    AutoRemove = true
                }
            }, cancellationToken)).ID;

            MultiplexedStream? stream = null;
            ActiveContainer container;
            try {
                stream = await _dockerClient.Containers.AttachContainerAsync(containerId, tty: false, new ContainerAttachParameters {
                    Stream = true,
                    Stdin = true,
                    Stdout = true
                }, cancellationToken);

                await _dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
                container = new ActiveContainer(containerId, stream);

                var outputBuffer = ArrayPool<byte>.Shared.Rent(2048);
                try {
                    var result = await _warmupExecutionProcessor.ExecuteInContainerAsync(
                        container, _warmupAssemblyBytes, outputBuffer, includePerformance: false, cancellationToken
                    );
                    if (!result.IsOutputReadSuccess)
                        throw new Exception($"Warmup output failed for container {containerName}:\r\n" + Encoding.UTF8.GetString(result.Output.Span) + Encoding.UTF8.GetString(result.OutputReadFailureMessage.Span));
                }
                finally {
                    ArrayPool<byte>.Shared.Return(outputBuffer);
                }

                _logger.LogDebug($"Allocated container {containerName}");
            }
            catch {
                _containerCleanup.QueueForCleanup(containerId, stream);
                throw;
            }

            return container;
        }
    }
}
