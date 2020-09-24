using System;
using System.Collections.Generic;
using System.Windows.Forms;
using iphonebackupbrowser;
using System.IO;
using mbdbdump;
using System.Drawing;
using System.Threading;

namespace WechatExport
{
    public partial class Form1 : Form
    {
        private List<MBFileRecord> files92;
        
        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        private void LoadManifests()
        {
            comboBox1.Items.Clear();
            string s = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            s = MyPath.Combine(s, "Apple Computer", "MobileSync", "Backup");
            try
            {
                DirectoryInfo d = new DirectoryInfo(s);
                int numberOfBackups = 0;
                foreach (DirectoryInfo sd in d.GetDirectories())
                {
                    IPhoneBackup backup = LoadManifest(sd.FullName);
                    if (backup != null)
                    {
                        comboBox1.Items.Add(backup);
                        ++numberOfBackups;
                    }
                }

                if (numberOfBackups == 0)
                {
                    System.Threading.Timer timer = null;
                    timer = new System.Threading.Timer((obj) =>
                    {
                        MessageBox.Show("没有找到iTunes备份文件夹，可能需要手动选择。", "提示");
                        timer.Dispose();
                    }, null, 100, System.Threading.Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                listBox1.Items.Add(ex.ToString());
            }
            comboBox1.Items.Add("<选择其他备份文件夹...>");
        }

        private IPhoneBackup LoadManifest(string path)
        {
            IPhoneBackup backup = null;
            string filename = Path.Combine(path, "Info.plist");
            try
            {
                xdict dd = xdict.open(filename);
                if (dd != null)
                {
                    backup = new IPhoneBackup
                    {
                        path = path
                    };
                    foreach (xdictpair p in dd)
                    {
                        if (p.item.GetType() == typeof(string))
                        {
                            switch (p.key)
                            {
                                case "Device Name": backup.DeviceName = (string)p.item; break;
                                case "Display Name": backup.DisplayName = (string)p.item; break;
                                case "Last Backup Date":
                                    DateTime.TryParse((string)p.item, out backup.LastBackupDate);
                                    break;
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.InnerException.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            return backup;
        }

        private void LoadCurrentBackup()
        {
            if (comboBox1.SelectedItem.GetType() != typeof(IPhoneBackup))
                return;
            var backup = (IPhoneBackup)comboBox1.SelectedItem;

            files92 = null;
            try
            {
                if (File.Exists(Path.Combine(backup.path, "Manifest.mbdb")))
                {
                    files92 = mbdbdump.mbdb.ReadMBDB(backup.path, "com.tencent.xin");
                }
                else if (File.Exists(Path.Combine(backup.path, "Manifest.db")))
                {
                    files92 = V10db.ReadMBDB(Path.Combine(backup.path, "Manifest.db"), "com.tencent.xin");
                }
                if (files92 != null && files92.Count > 0)
                {
                    label2.Text = "正确";
                    label2.ForeColor = Color.Green;
                    button2.Enabled = true;
                }
                else
                {
                    label2.Text = "未找到";
                    label2.ForeColor = Color.Red;
                    button2.Enabled = false;
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.InnerException.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            }
        }

        private void BeforeLoadManifest()
        {
            comboBox1.SelectedIndex = -1;
            label2.Text = "未选择";
            label2.ForeColor = Color.Black;
            button2.Enabled = false;
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            BeforeLoadManifest();
            LoadManifests();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.ClientSize = new Size(groupBox2.Left * 2 + groupBox2.Width, groupBox2.Top + groupBox2.Height + groupBox1.Top);
            textBox1.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
#if DEBUG
            textBox1.Text = "D:\\pngs\\bak\\";
#endif
            this.button1.PerformClick();
            // Button1_Click(null, null);
        }

        private void ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

            if (comboBox1.SelectedIndex == -1)
                return;
            if (comboBox1.SelectedItem.GetType() == typeof(IPhoneBackup))
            {
                LoadCurrentBackup();
                return;
            }
            OpenFileDialog fd = new OpenFileDialog
            {
                Filter = "iPhone Backup|Info.plist|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };
            if (fd.ShowDialog() == DialogResult.OK)
            {
                BeforeLoadManifest();
                IPhoneBackup b = LoadManifest(Path.GetDirectoryName(fd.FileName));
                if (b != null)
                {
                    b.custom = true;
                    comboBox1.Items.Insert(comboBox1.Items.Count - 1, b);
                    comboBox1.SelectedIndex = comboBox1.Items.Count - 2;
                }
            }
        }

        private void RadioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton1.Checked)
            {
                textBox1.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
        }

        private void Button3_Click(object sender, EventArgs e)
        {
            radioButton2.Checked = true;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                textBox1.Text = folderBrowserDialog1.SelectedPath;
            if (textBox1.Text == Environment.GetFolderPath(Environment.SpecialFolder.Desktop))
                radioButton1.Checked = true;
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
            groupBox1.Enabled = groupBox3.Enabled = groupBox4.Enabled = false;
            button2.Enabled = false;
            new Thread(new ThreadStart(Run)).Start();
        }

        class Logger : WeChatInterface.ILogger
        {
            ListBox listBox;

            public Logger(ListBox listBox)
            {
                this.listBox = listBox;
            }

            public void AddLog(String log)
            {
                listBox.Items.Add(log);
                listBox.TopIndex = listBox.Items.Count - 1;
            }

            public void Debug(string log)
            {
                AddLog(log);
            }
        }
       

        void Run()
        {
            var saveBase = textBox1.Text;

            WeChatInterface.ILogger logger = new Logger(this.listBox1);
            bool toHtml = radioButton3.Checked;
            string indexPath = Path.Combine(saveBase, "index.html");

            WeChatInterface.Export(((IPhoneBackup)comboBox1.SelectedItem).path, saveBase, indexPath, toHtml, files92, logger);
         
            try
            {
                if (toHtml) System.Diagnostics.Process.Start(indexPath);
            }
            catch (Exception) { }
            groupBox1.Enabled = groupBox3.Enabled = groupBox4.Enabled = true;
            button2.Enabled = true;
           
            MessageBox.Show("处理完成", "提示");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(0);
        }
    }
}
