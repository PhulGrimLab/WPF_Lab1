using System.Text;
using Ude;
class Program
{
    static void Main(string[] args)
    {
        // 인자 또는 사용자 입력으로 디렉토리 설정
        string dir;
        if (args.Length > 0 && Directory.Exists(args[0]))
            dir = args[0];
        else
        {
            Console.Write("대상 디렉토리 입력 (엔터시 현재 디렉토리): ");
            var input = Console.ReadLine();
            dir = !string.IsNullOrWhiteSpace(input) && Directory.Exists(input) ? input : Directory.GetCurrentDirectory();
        }

        string[] exts = { ".cs", ".cpp", ".h" };

        var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                             .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            string detected = DetectEncoding(file, out Encoding srcEncoding);

            // 이미 UTF-8 BOM이면 스킵
            if (srcEncoding is UTF8Encoding u && u.GetPreamble().Length > 0)
            {
                Console.WriteLine($"{Path.GetFileName(file)} : Already UTF-8 with BOM");
                continue;
            }

            string text = File.ReadAllText(file, srcEncoding ?? Encoding.Default);
            File.WriteAllText(file, text, new UTF8Encoding(true));
            Console.WriteLine($"{Path.GetFileName(file)} : Converted to UTF-8 with BOM");
        }
    }

    static string DetectEncoding(string path, out Encoding encoding)
    {
        using (FileStream fs = File.OpenRead(path))
        {
            CharsetDetector detector = new CharsetDetector();
            detector.Feed(fs);
            detector.DataEnd();

            if (detector.Charset != null)
            {
                encoding = Encoding.GetEncoding(detector.Charset);
                return detector.Charset;
            }
            else
            {
                encoding = Encoding.Default;
                return "Unknown";
            }
        }
    }
}