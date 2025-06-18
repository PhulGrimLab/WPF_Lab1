using System;
using System.IO;
using System.Linq;
using System.Text;
using Ude;          // Nuget에서 Ude.NetStandard 찾아서 설치

namespace EncodingConverterConsoleV0
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 인자 또는 사용자 입력으로 디렉토리 설정
            string dir;

            if (args.Length > 0 && Directory.Exists(args[0]))
            {
                dir = args[0];
            }
            else
            {
                Console.Write("대상 디렉토리 입력 (엔터시 현재 디렉토리): ");
                var input = Console.ReadLine();
                dir = (!string.IsNullOrWhiteSpace(input)) && (Directory.Exists(input)) ? input : Directory.GetCurrentDirectory();
            }

            string[] exts = { ".cs", ".cpp", ".h", ".xaml" };

            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                                 .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                string detected = DetectEncoding(file, out Encoding srcEncoding);

                // 이미 UTF-8 BOM이면 스킵
                if (HasUtf8Bom(file))
                {
                    Console.WriteLine($"{Path.GetFileName(file)} : Already UTF-8 with BOM");
                    continue;
                }

                // 파일을 바이트로 읽고, 감지된 인코딩으로 디코딩
                byte[] bytes = File.ReadAllBytes(file);
                string text;
                try
                {
                    // Ude가 ASCII로 감지했지만, 한글 등 비ASCII 문자가 있을 수 있으므로 예외 처리
                    text = srcEncoding.GetString(bytes);
                }
                catch
                {
                    // 디코딩 실패 시 시스템 기본 인코딩으로 재시도
                    text = Encoding.Default.GetString(bytes);
                }

                // 변환된 내용을 UTF-8 BOM으로 저장
                File.WriteAllText(file, text, new UTF8Encoding(true));

                Console.WriteLine($"{Path.GetDirectoryName(file)}{Path.GetFileName(file)} : Converted to UTF-8 with BOM");
            }
        }

        static bool HasUtf8Bom(string filePath)
        {
            byte[] bom = new byte[3];

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length < 3)
                {
                    return false;
                }

                fs.Read(bom, 0, 3);
            }

            return bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF;
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
                    try
                    {
                        encoding = Encoding.GetEncoding(detector.Charset);
                    }
                    catch
                    {
                        encoding = Encoding.Default;
                    }
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
}
