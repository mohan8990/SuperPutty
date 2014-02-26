﻿/*
 * Copyright (c) 2009 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using SuperPutty.Data;
using log4net;

namespace SuperPutty
{
    public partial class RemoteFileListPanel : ToolWindowDocument
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(RemoteFileListPanel));

        private DockPanel m_DockPanel;
        private PscpTransfer m_Transfer;
        private SessionData m_Session;
        private string m_Path = ".";
        private dlgMouseFeedback m_MouseFollower;
        public RemoteFileListPanel(PscpTransfer transfer, DockPanel dockPanel, SessionData session)
        {
            Log.InfoFormat("Started new File Transfer Session for {0}", session.SessionName);
            m_Session = session;
            m_DockPanel = dockPanel;
            m_Transfer = transfer;
            m_MouseFollower = new dlgMouseFeedback();
            InitializeComponent();
            
            this.TabText = session.SessionName;

            LoadDirectory(m_Path);
        }

        private bool LoadDirectory(string path)
        {
            Log.InfoFormat("Request directory listing for '{0}/{1}'", m_Session.SessionName, path);
            DirListingCallback dirCallback = delegate(RequestResult result, List<FileEntry> files)
            {
                switch (result)
                {
                    case RequestResult.RetryAuthentication:
                        this.BeginInvoke(new Action(() =>
                        {
                            dlgLogin m_Login = new dlgLogin(m_Session);
                            if (m_Login.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                            {
                                m_Session.Username = m_Login.Username;
                                m_Session.Password = m_Login.Password;
                                LoadDirectory(path);
                            }
                            else
                            {
                                this.Close();
                            }
                        }));

                        break;
                    case RequestResult.ListingFollows:
                        Log.DebugFormat("Remote host returned {0} File entries", files.Count);
                        RefreshListView(files);
                        break;
                    case RequestResult.UnknownError:
                        Log.Info("Unknown Error trying to get file listing");
                        break;
                    case RequestResult.InvalidArguments:
                        Log.Info("Invalid Arguments Passed to scp");
                        break;
                    case RequestResult.SessionInvalid:
                        Log.Info("Session is invalid");
                        break;
                    case RequestResult.CancelLogin:
                        Log.Info("User cancel login");
                        this.BeginInvoke(new MethodInvoker(this.Close));
                        break;
                    default:
                        Log.InfoFormat("Unknown result '{0}'", result);
                        break;
                }
            };

            m_Transfer.BeginGetDirectoryListing(path, dirCallback);                       
            return true;
        }

        private void RefreshListView(List<FileEntry> files)
        {
            if (this.IsDisposed) return;
            if (listView1.InvokeRequired)
            {
                listView1.BeginInvoke((MethodInvoker)delegate()
                {
                    RefreshListView(new List<FileEntry>(files));
                });
            }
            else
            {
                lock (listView1)
                {
                    listView1.BeginUpdate();
                    listView1.Items.Clear();
                    foreach (FileEntry file in files)
                    {
                        if (file.Name.Equals("."))
                            continue;

                        ListViewItem addedItem = listView1.Items.Add(file.Name, file.Name);
                        addedItem.ImageIndex = file.IsFile ? 1 : 0;
                        addedItem.Tag = file;
                        addedItem.SubItems.Add(new ListViewItem.ListViewSubItem(addedItem, file.TimeStamp.ToLocalTime().ToString()));
                        addedItem.SubItems.Add(new ListViewItem.ListViewSubItem(addedItem, String.Format("{0} KB", file.BlockCount / 1024)));
                        addedItem.SubItems.Add(new ListViewItem.ListViewSubItem(addedItem, file.OwnerName));
                        addedItem.SubItems.Add(new ListViewItem.ListViewSubItem(addedItem, file.GroupName));
                        addedItem.SubItems.Add(new ListViewItem.ListViewSubItem(addedItem, file.PermissionString));
                    }
                    listView1.EndUpdate();
                }
            }
        }

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count != 0)
            {
                FileEntry fe = (FileEntry)listView1.SelectedItems[0].Tag;
                if (fe.IsFolder)
                {
                    if (fe.Name == "..")
                    {
                        // strip off last path part (not handling escaped / case...)
                        int idx = m_Path.LastIndexOf('/');
                        if (idx != -1)
                        {
                            m_Path = m_Path.Substring(0, idx);
                        }
                        else
                        {
                            m_Path += "/" + fe.Name;
                        }
                    }
                    else
                    {
                        m_Path += "/" + fe.Name;
                    }
                    LoadDirectory(m_Path);
                }
            }
        }


        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            LoadDirectory(m_Path);
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #region ListView Modes
        private void detailsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.View = View.Details;
        }

        private void smallIconsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.View = View.SmallIcon;
        }

        private void largeIconsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.View = View.LargeIcon;
        }

        private void tileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.View = View.Tile;
        }

        private void listToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listView1.View = View.List;
        } 
        #endregion

        #region Drag Drop
        private void listView1_DragOver(object sender, DragEventArgs e)
        {
            ListView listView = sender as ListView;
            // convert global screen position to control position
            Point p = listView1.PointToClient(new Point(e.X, e.Y));

            if (m_MouseFollower.Visible)
            {
                m_MouseFollower.Location = new Point(e.X + 5, e.Y + 5);
            }

            ListViewHitTestInfo hti = listView.HitTest(p.X, p.Y);
            if (hti.Item != null)
            {
                e.Effect = DragDropEffects.All;
                if (hti.Item.ImageIndex == 0)
                    hti.Item.Selected = true;
            }
            else
            {
                listView.SelectedItems.Clear();
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void listView1_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            ListView listView = (ListView)sender;

            Point targetPoint = listView.PointToClient(new Point(e.X, e.Y));

            ListViewItem dropTarget = listView.GetItemAt(targetPoint.X, targetPoint.Y);
            string target = this.m_Path;
            if (dropTarget != null && dropTarget.ImageIndex == 0)
            {
                target += "/" + dropTarget.Text;
            }

            long totalBytes = 0;
            float transferredBytes = 0;
            int fileCount = 0;
            int currentFileNum = 0;
            foreach (string file in files)
            {
                bool IsFile = File.Exists(file);
                bool IsDir = Directory.Exists(file);
                if (File.Exists(file))
                {
                    FileInfo fi = new FileInfo(file);
                    totalBytes += fi.Length;
                    fileCount++;
                }
                else if (Directory.Exists(file))
                {
                    RecurseDir(file, ref totalBytes, ref fileCount);
                }
                else
                    Log.WarnFormat("Dropped Unknown {0} on {1}", file, target);
            }
            //Logger.Log("Total Bytes: {0} Total Files: {1}", totalBytes, fileCount);
            frmTransferStatus frmStatus = new frmTransferStatus();
            frmStatus.Text = "Uploading files to " + m_Session.SessionName;
            frmStatus.Show(m_DockPanel, DockState.DockBottom);

            TransferUpdateCallback callback = delegate(bool fileComplete, bool cancelTransfer, FileTransferStatus status)
            {
                if (cancelTransfer)
                {
                    Log.Info("User requested to Cancel Transfer");
                    m_Transfer.CancelTransfers();
                    frmStatus.Close();
                    LoadDirectory(target);
                    return;
                }
                if (fileComplete)
                {
                    currentFileNum++;
                    transferredBytes += status.BytesTransferred;
                    //Logger.Log("Transfered: {0}/{1} kB {2} of {3} files", transferredBytes, totalBytes / 1024, currentFileNum, fileCount);
                }
                frmStatus.UpdateProgress(status, (int)transferredBytes, (int)totalBytes / 1024, currentFileNum, fileCount);
                if (currentFileNum >= fileCount)
                {
                    LoadDirectory(target);
                }
            };
            frmStatus.m_callback = callback;
            m_Transfer.BeginCopyFiles(files, target, callback);
        }

        public static void RecurseDir(string sourceDir, ref long bytes, ref int count)
        {
            // Process the list of files found in the directory.
            string[] fileEntries = Directory.GetFiles(sourceDir);
            foreach (string fileName in fileEntries)
            {
                FileInfo f = new FileInfo(fileName);
                bytes += f.Length;
                count++;
            }

            // Recurse into subdirectories of this directory.
            string[] subdirEntries = Directory.GetDirectories(sourceDir);
            foreach (string subdir in subdirEntries)
                // Do not iterate through reparse points
                if ((File.GetAttributes(subdir) &
                     FileAttributes.ReparsePoint) !=
                         FileAttributes.ReparsePoint)
                {
                    RecurseDir(subdir, ref bytes, ref count);
                }
        }

        private void listView1_DragEnter(object sender, DragEventArgs e)
        {
            //m_MouseFollower.Show();
        }

        private void listView1_DragLeave(object sender, EventArgs e)
        {
            //m_MouseFollower.Hide();
        }

        private void listView1_MouseMove(object sender, MouseEventArgs e)
        {
            //if (m_MouseFollower.Visible && e.Button == MouseButtons.Left)
            //{
            //    m_MouseFollower.Location = new Point(e.X, e.Y);
            //    Logger.Log("Move Mouse");
            //}
        }
        
        #endregion
    }
}
