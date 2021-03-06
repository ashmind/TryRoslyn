using System;
using System.Diagnostics;

namespace SharpLab.Container.Manager.Internal {
    public class ContainerNameFormat {
        private readonly string _processId;

        public ContainerNameFormat() {
            using var process = Process.GetCurrentProcess();
            _processId = process.Id.ToString();
        }

        public string GeneratePreallocatedName() {
            return $"sharplab_{_processId}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_ffffff}";
        }

        public string GenerateSessionContainerName(string sessionId) {
            return $"sharplab_session_{sessionId}_{_processId}";
        }

        public bool IsNameFromPreviousManager(string containerName) {
            return containerName.StartsWith("/sharplab_")
                && !containerName.AsSpan().Slice("/sharplab_".Length).StartsWith(_processId, StringComparison.Ordinal);
        }
    }
}
