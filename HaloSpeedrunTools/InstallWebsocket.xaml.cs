using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Forms;

namespace HaloSpeedrunTools
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class InstallWebsocket : Window
    {
        public InstallWebsocket()
        {
            InitializeComponent();

            pathURLText.Text = "C:\\Program Files\\obs-studio";
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                pathURLText.Text = dialog.SelectedPath;
            }
        }

        private void InstallOBSWS_Click(object sender, RoutedEventArgs e)
        {
            InstallOBSWS.IsEnabled = false;
            InstallOBSWS.Content = "Downloading...";

            WebClient webClient = new WebClient();
            webClient.Headers.Add("Accept: text/html, application/xhtml+xml, */*");
            webClient.Headers.Add("User-Agent: Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; WOW64; Trident/5.0)");
            webClient.DownloadFile(new Uri("https://github.com/Palakis/obs-websocket/releases/download/4.9.0/obs-websocket-4.9.0-Windows.zip"), "temp.zip");

            InstallOBSWS.Content = "Installing...";
            ZipArchiveExtensions.ExtractToDirectory(ZipFile.OpenRead("temp.zip"), pathURLText.Text,true);
            System.IO.File.Delete("temp.zip");

            InstallOBSWS.Content = "Complete! Restart OBS.";
        }
    }
    public static class ZipArchiveExtensions
    {
        public static void ExtractToDirectory(this ZipArchive archive, string destinationDirectoryName, bool overwrite)
        {
            if (!overwrite)
            {
                archive.ExtractToDirectory(destinationDirectoryName);
                return;
            }

            DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
            string destinationDirectoryFullPath = di.FullName;

            foreach (ZipArchiveEntry file in archive.Entries)
            {
                string completeFileName = System.IO.Path.GetFullPath(System.IO.Path.Combine(destinationDirectoryFullPath, file.FullName));

                if (!completeFileName.StartsWith(destinationDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Trying to extract file outside of destination directory. See this link for more info: https://snyk.io/research/zip-slip-vulnerability");
                }

                if (file.Name == "")
                {// Assuming Empty for Directory
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(completeFileName));
                    continue;
                }
                file.ExtractToFile(completeFileName, true);
            }
            archive.Dispose();
        }
    }
}
