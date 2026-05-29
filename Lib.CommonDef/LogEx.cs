using System.IO;
using System.Diagnostics;

namespace Lib.CommonDef
{
    public enum LogType
    {
        TRACE,          // 일반적인 로그
        DEBUG,          // Debug, Verbos 모드
        ERROR           // 에러, 익셉션 로그
    }

    public class LogEx
    {
        static readonly string _FolderPulse_Log = "C:\\FolderPulse_Log\\";

        static readonly Object tracewrite = new Object();
        static readonly Object debugwrite = new Object();
        static readonly Object errorwrite = new Object();

        static private void SaveFile(LogType LT, String msg)
        {
            if (!Directory.Exists(_FolderPulse_Log))
            {
                Directory.CreateDirectory(_FolderPulse_Log);
            }

            // 현재 시간은 여기서 한번만 구한다.
            DateTime NowTime = DateTime.Now;

            String log = String.Format("[{0}] : {1}", NowTime.ToString("yyyy-MM-dd HH:mm:ss.fff"), msg);

            String path = string.Empty;

            String NowTimeyyyyMMdd = NowTime.ToString("yyyyMMdd");

            String processName = Process.GetCurrentProcess().ProcessName;

            switch (LT)
            {
                case LogType.TRACE:
                    path = String.Format("{0}{1}_{2}_Trace.txt", _FolderPulse_Log, NowTimeyyyyMMdd, processName);
                    lock (tracewrite)
                    {
                        WriteFile(log, path);
                    }
                    break;
                case LogType.DEBUG:
                    path = String.Format("{0}{1}_{2}_Debug.txt", _FolderPulse_Log, NowTimeyyyyMMdd, processName);
                    lock (debugwrite)
                    {
                        WriteFile(log, path);
                    }
                    break;
                case LogType.ERROR:
                    path = String.Format("{0}{1}_{2}_Error.txt", _FolderPulse_Log, NowTimeyyyyMMdd, processName);
                    lock (errorwrite)
                    {
                        WriteFile(log, path);
                    }
                    break;
                default:
                    break;
            }
        }

        static private void WriteFile(string msg, string path)
        {
            StreamWriter file = new StreamWriter(path, true);
                        
            file.WriteLine(String.Format("{0}", msg));
            
            file.Close();

            Trace.WriteLine(msg);
        }


        static public void D(String msg)
        {
            SaveFile(LogType.DEBUG, msg);
        }

        static public void T(String msg)
        {
            SaveFile(LogType.TRACE, msg);
        }

        static public void Error(String msg)
        {
            SaveFile(LogType.ERROR, msg);
        }
    }
}
