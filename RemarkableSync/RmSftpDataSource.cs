﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;


namespace RemarkableSync
{
    public partial class RmSftpDataSource : IRmDataSource
    {
        public static readonly string SshHostConfig = "SshHost";

        public static readonly string SshPasswordConfig = "SshPassword";

        private static readonly string ContentFolderPath = "/home/root/.local/share/remarkable/xochitl";

        private SftpClient _client;

        public RmSftpDataSource(IConfigStore configStore)
        {
            string host = configStore.GetConfig(SshHostConfig);
            string password = configStore.GetConfig(SshPasswordConfig);

            host = host ?? "10.11.99.1";
            if (password == null)
            {
                string errMsg = "Unable to get SSH password from config store";
                Logger.LogMessage($"{errMsg}");
                throw new Exception(errMsg);
            }

            const string username = "root";
            const int port = 22;

            try
            {
                _client = new SftpClient(host, port, username, password);
                _client.Connect();
            }
            catch (SshAuthenticationException err)
            {
                Logger.LogMessage($"authentication error on SFTP connection: err: {err.Message}");
                throw new Exception("reMarkable device SSH login failed");
            }
            catch (Exception err)
            {
                Logger.LogMessage($"error on SFTP connection: err: {err.Message}");
                throw new Exception("Failed to connect to reMarkable device via SSH");
            }
        }

        public async Task<List<RmItem>> GetItemHierarchy()
        {
            List<RmItem> collection = await Task.Run(GetAllItems);
            return getChildItemsRecursive("", ref collection);
        }

        public async Task<RmDownloadedDoc> DownloadDocument(RmItem item)
        {
            return await Task.Run(() =>
            {
                // get the .content file for the notebook first
                string contentFileFullPath = $"{ContentFolderPath}/{item.ID}.content";
                MemoryStream stream = new MemoryStream();
                _client.DownloadFile(contentFileFullPath, stream);
                NotebookContent notebookContent = NotebookContent.FromStream(stream);
                List<string> pageIDs = notebookContent.pages.ToList();
                Logger.LogMessage($"Notebook \"{item.VissibleName}\" has {pageIDs.Count} pages");

                RmSftpDownloadedDoc downloadedDoc = new RmSftpDownloadedDoc(ContentFolderPath, item.ID, pageIDs, _client);
                return downloadedDoc;
            });
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        private List<RmItem> GetAllItems()
        {
            List<RmItem> items = new List<RmItem>();

            foreach (var file in _client.ListDirectory(ContentFolderPath))
            {
                if (file.IsDirectory)
                    continue;

                if (!file.Name.EndsWith(".metadata"))
                    continue;

                MemoryStream stream = new MemoryStream();
                _client.DownloadFile(file.FullName, stream);
                NotebookMetadata notebookMetadata = NotebookMetadata.FromStream(stream);

                if (notebookMetadata.deleted)
                    continue;

                items.Add(notebookMetadata.ToRmItem(file.Name));
            }

            return items;
        }

        private List<RmItem> getChildItemsRecursive(string parentId, ref List<RmItem> items)
        {
            var children = (from item in items where item.Parent == parentId select item).ToList();
            foreach (var child in children)
            {
                child.Children = getChildItemsRecursive(child.ID, ref items);
            }
            return children;
        }
    }
}

