using System.Collections.Generic;
using System.Threading.Tasks;

namespace Caldera
{
    public class CompilerInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsMsvc => Name.Contains("cl.exe");
        public bool IsWsl => Name.StartsWith("WSL");
    }

    public static class CompilerDiscovery
    {
        public static List<CompilerInfo> Discovered { get; private set; } = new();

        public static async Task DiscoverAsync()
        {
            var list = new List<CompilerInfo>();

            // 1. Clang (Windows)
            var clangExists = await CheckExe("clang++", "--version");
            list.Add(new CompilerInfo { Name = "clang++", IsAvailable = clangExists, Path = clangExists ? "clang++" : "" });

            // 2. GCC (Windows)
            var gccExists = await CheckExe("g++", "--version");
            list.Add(new CompilerInfo { Name = "g++", IsAvailable = gccExists, Path = gccExists ? "g++" : "" });

            // 3. MSVC
            var clPath = CompilerService.FindVcvars64Anywhere();
            list.Add(new CompilerInfo { Name = "cl.exe", IsAvailable = clPath != null, Path = clPath ?? "" });

            // 4. WSL Clang
            var wslClangExists = await CheckExe("wsl", "-e clang++ --version");
            list.Add(new CompilerInfo { Name = "WSL clang++", IsAvailable = wslClangExists, Path = wslClangExists ? "wsl" : "" });

            // 5. WSL GCC
            var wslGccExists = await CheckExe("wsl", "-e g++ --version");
            list.Add(new CompilerInfo { Name = "WSL g++", IsAvailable = wslGccExists, Path = wslGccExists ? "wsl" : "" });

            Discovered = list;
        }

        private static async Task<bool> CheckExe(string exe, string args)
        {
            try
            {
                var (_, _, code) = await CompilerService.RunProcessAsync(exe, args);
                return code == 0;
            }
            catch { return false; }
        }
    }
}
