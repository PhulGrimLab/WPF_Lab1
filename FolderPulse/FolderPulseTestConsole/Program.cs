using System.Security.Cryptography;
using System.Text;

namespace FolderPulseTestConsole
{
    internal class Program
    {
        private const string ConfigFileName = "FolderPulseTestConsole.ini";

        static async Task Main(string[] args)
        {
            string exeFolder = AppContext.BaseDirectory;
            string configPath = Path.Combine(exeFolder, ConfigFileName);

            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, AppSettings.CreateDefaultIni(), Encoding.UTF8);
                Console.WriteLine($"Created default config: {configPath}");
            }

            AppSettings settings = AppSettings.Load(configPath);
            settings.Validate();

            using CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine("FolderPulse test file generator");
            Console.WriteLine($"Output folder : {exeFolder}");
            Console.WriteLine($"Config file   : {configPath}");
            Console.WriteLine($"File count    : {settings.FileCount}");
            Console.WriteLine($"Interval      : {settings.IntervalMilliseconds} ms");
            Console.WriteLine($"Size range    : {settings.MinFileSizeBytes:N0} ~ {settings.MaxFileSizeBytes:N0} bytes");
            Console.WriteLine("Press Ctrl+C to stop.");
            Console.WriteLine();

            for (int i = 1; i <= settings.FileCount; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                int fileSize = RandomNumberGenerator.GetInt32(
                    settings.MinFileSizeBytes,
                    settings.MaxFileSizeBytes + 1);

                string fileName = CreateFileName(settings, i);
                string filePath = Path.Combine(exeFolder, fileName);

                WriteRandomTextFile(filePath, fileSize);
                Console.WriteLine($"[{i}/{settings.FileCount}] Created {fileName} ({fileSize:N0} bytes)");

                if (i < settings.FileCount)
                {
                    await Task.Delay(settings.IntervalMilliseconds, cts.Token);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Done.");
        }

        private static string CreateFileName(AppSettings settings, int index)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            return $"{settings.FileNamePrefix}_{timestamp}_{index:D4}{settings.FileExtension}";
        }

        private static void WriteRandomTextFile(string filePath, int sizeBytes)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789\r\n ";

            char[] buffer = new char[sizeBytes];
            byte[] randomBytes = RandomNumberGenerator.GetBytes(sizeBytes);

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = chars[randomBytes[i] % chars.Length];
            }

            File.WriteAllText(filePath, new string(buffer), Encoding.ASCII);
        }
    }

    internal sealed class AppSettings
    {
        public int IntervalMilliseconds { get; private set; } = 1000;
        public int FileCount { get; private set; } = 10;
        public int MinFileSizeBytes { get; private set; } = 1024;
        public int MaxFileSizeBytes { get; private set; } = 10240;
        public string FileNamePrefix { get; private set; } = "FolderPulseTest";
        public string FileExtension { get; private set; } = ".txt";

        public static AppSettings Load(string configPath)
        {
            AppSettings settings = new AppSettings();

            foreach (string rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith(';') ||
                    line.StartsWith('#') ||
                    line.StartsWith('['))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line[..separatorIndex].Trim();
                string value = line[(separatorIndex + 1)..].Trim();

                settings.ApplyValue(key, value);
            }

            return settings;
        }

        public static string CreateDefaultIni()
        {
            return """
                   [Generator]
                   ; File creation interval in milliseconds.
                   IntervalMilliseconds=1000

                   ; Total number of files to create.
                   FileCount=10

                   ; Random text file size range in bytes.
                   MinFileSizeBytes=1024
                   MaxFileSizeBytes=10240

                   ; Generated file name format:
                   ; {FileNamePrefix}_yyyyMMdd_HHmmss_fff_0001{FileExtension}
                   FileNamePrefix=FolderPulseTest
                   FileExtension=.txt
                   """;
        }

        public void Validate()
        {
            if (IntervalMilliseconds < 0)
            {
                throw new InvalidOperationException("IntervalMilliseconds must be 0 or greater.");
            }

            if (FileCount <= 0)
            {
                throw new InvalidOperationException("FileCount must be greater than 0.");
            }

            if (MinFileSizeBytes < 0)
            {
                throw new InvalidOperationException("MinFileSizeBytes must be 0 or greater.");
            }

            if (MaxFileSizeBytes < MinFileSizeBytes)
            {
                throw new InvalidOperationException("MaxFileSizeBytes must be greater than or equal to MinFileSizeBytes.");
            }

            if (string.IsNullOrWhiteSpace(FileNamePrefix))
            {
                throw new InvalidOperationException("FileNamePrefix cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(FileExtension))
            {
                throw new InvalidOperationException("FileExtension cannot be empty.");
            }

            if (!FileExtension.StartsWith('.'))
            {
                FileExtension = "." + FileExtension;
            }
        }

        private void ApplyValue(string key, string value)
        {
            switch (key)
            {
                case nameof(IntervalMilliseconds):
                    IntervalMilliseconds = ParseInt(key, value);
                    break;
                case nameof(FileCount):
                    FileCount = ParseInt(key, value);
                    break;
                case nameof(MinFileSizeBytes):
                    MinFileSizeBytes = ParseInt(key, value);
                    break;
                case nameof(MaxFileSizeBytes):
                    MaxFileSizeBytes = ParseInt(key, value);
                    break;
                case nameof(FileNamePrefix):
                    FileNamePrefix = value;
                    break;
                case nameof(FileExtension):
                    FileExtension = value;
                    break;
            }
        }

        private static int ParseInt(string key, string value)
        {
            if (int.TryParse(value, out int result))
            {
                return result;
            }

            throw new InvalidOperationException($"{key} must be a valid integer. Current value: {value}");
        }
    }
}
