using System;
using System.Collections.Generic;
using System.IO;
using iphonebackupbrowser;
using mbdbdump;
using WechatExport;
using System.Threading.Tasks;

namespace wxexp
{
    class Program
    {
        class Logger : WeChatInterface.ILogger
        {
            
            public void AddLog(String log)
            {
                Console.WriteLine(log);
            }
        }

        static string ParseArg(string[] args, string name)
        {
            string value = null;
            if (name == null)
            {
                return value;
            }

            for (int idx = 0; idx < args.Length; ++idx)
            {
                if (name.Equals(args[idx]) && idx < args.Length - 1)
                {
                    if (args[idx + 1] != null && !args[idx + 1].StartsWith("--"))
                    {
                        value = args[idx + 1];
                    }
                    break;
                }
            }

            return value;
        }

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please enter valid argument.");
                return 1;
            }

            string backupPath = ParseArg(args, "--backup");
            string output = ParseArg(args, "--output"); ;

            if (backupPath == null || output == null)
            {
                Console.WriteLine("Please enter valid argument.");
                Console.WriteLine("Argument: --backup [iPhone backup path] --output [output path]");
                return 1;
            }

            var backup = WeChatInterface.LoadManifest(backupPath);

            if (backup == null)
            {
                return 1;
            }

            List<MBFileRecord> files92 = null;
            try
            {
                if (File.Exists(Path.Combine(backupPath, "Manifest.mbdb")))
                {
                    files92 = mbdbdump.mbdb.ReadMBDB(backupPath, "com.tencent.xin");
                }
                else if (File.Exists(Path.Combine(backupPath, "Manifest.db")))
                {
                    files92 = V10db.ReadMBDB(Path.Combine(backupPath, "Manifest.db"), "com.tencent.xin");
                }


            }
            catch (InvalidOperationException ex)
            {
                files92 = null;
                Console.Write(ex.InnerException.ToString());

            }
            catch (Exception ex)
            {
                files92 = null;
                Console.Write(ex.Message + "\n" + ex.StackTrace);
            }

            if (files92 == null || files92.Count <= 0)
            {
                Console.WriteLine("未找到。");
                return 1;
            }

            var saveBase = output;


            WeChatInterface.ILogger logger = new Logger();
            bool toHtml = true;
            string indexPath = Path.Combine(saveBase, "index.html");

            WeChatInterface.Export(backup.path, saveBase, indexPath, toHtml, files92, logger);


            Console.WriteLine("处理完成");
            return 0;
        }
    }
}
