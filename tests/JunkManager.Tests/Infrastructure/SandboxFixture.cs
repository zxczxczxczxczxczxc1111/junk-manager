using System.Diagnostics;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Every Sandbox test works inside one throwaway directory. It is removed on
/// dispose even when a test fails, because leftover junctions on a developer
/// machine are exactly the kind of debris that later gets deleted by accident.
/// </summary>
public sealed class SandboxFixture : IDisposable
{
    public SandboxFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "JunkManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string name, string content = "junk")
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateJunction(string name, string target)
    {
        var link = Path.Combine(Root, name);

        // mklink /J needs no elevation, unlike a symbolic link. That matters:
        // these tests must run on the developer machine without admin.
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("не удалось запустить cmd для mklink");

        var stderr = proc.StandardError.ReadToEnd();
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"mklink /J не сработал, код {proc.ExitCode}: {stderr}{stdout}");
        }

        return link;
    }

    public void Dispose()
    {
        try
        {
            RemoveTree(Root);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone: nothing to do, and this is not an error.
        }
        catch (IOException ex)
        {
            // Loud on purpose: leftover test debris is a real problem, not noise.
            Console.Error.WriteLine($"песочница не убралась: {Root}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"песочница не убралась: {Root}: {ex.Message}");
        }
    }

    /// <summary>
    /// Walks the tree by hand and removes every junction as a link, never as a
    /// directory to descend into. This is not caution for its own sake:
    /// Directory.EnumerateDirectories with SearchOption.AllDirectories DOES walk
    /// through a junction, so a cleanup written the obvious way would enumerate
    /// whatever the link points at, and these tests deliberately point one at
    /// System32.
    /// </summary>
    private static void RemoveTree(string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var info = new DirectoryInfo(sub);
            if (info.LinkTarget is not null)
            {
                info.Delete();
                continue;
            }

            RemoveTree(sub);
        }

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            // Флаг снимается перед удалением, и это не забота о красоте. Тест на
            // очистку read-only файла оставляет такой файл в песочнице всякий
            // раз, когда падает; без этой строки уборка отказывает, мусор
            // копится в %TEMP%, а следующий прогон начинается не с чистого листа.
            var svedeniya = new FileInfo(file);
            if ((svedeniya.Attributes & FileAttributes.ReadOnly) != 0)
            {
                svedeniya.Attributes &= ~FileAttributes.ReadOnly;
            }

            svedeniya.Delete();
        }

        Directory.Delete(dir);
    }
}
