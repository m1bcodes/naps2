/*
    NAPS2 (Not Another PDF Scanner 2)
    http://sourceforge.net/projects/naps2/
    
    Copyright (C) 2009       Pavel Sorejs
    Copyright (C) 2012       Michael Adams
    Copyright (C) 2013       Peter De Leeuw
    Copyright (C) 2012-2015  Ben Olden-Cooligan

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
*/

#region Usings

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAPS2.Config;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.ImportExport.Images;
using NAPS2.ImportExport.Pdf;
using NAPS2.Lang;
using NAPS2.Lang.Resources;
using NAPS2.Ocr;
using NAPS2.Operation;
using NAPS2.Recovery;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Images;
using NAPS2.Scan.Wia;
using NAPS2.Update;
using NAPS2.Util;

#endregion

namespace NAPS2.WinForms
{
    public partial class FDesktop : FormBase, IAutoUpdaterClient
    {
        #region Dependencies

        private readonly IEmailer emailer;
        private readonly IScannedImageImporter scannedImageImporter;
        private readonly StringWrapper stringWrapper;
        private readonly AppConfigManager appConfigManager;
        private readonly RecoveryManager recoveryManager;
        private readonly AutoUpdaterUI autoUpdaterUI;
        private readonly OcrDependencyManager ocrDependencyManager;
        private readonly IProfileManager profileManager;
        private readonly IScanPerformer scanPerformer;
        private readonly IScannedImagePrinter scannedImagePrinter;
        private readonly ChangeTracker changeTracker;
        private readonly EmailSettingsContainer emailSettingsContainer;
        private readonly FileNamePlaceholders fileNamePlaceholders;
        private readonly ImageSettingsContainer imageSettingsContainer;
        private readonly PdfSettingsContainer pdfSettingsContainer;
        private readonly StillImage stillImage;
        private readonly IOperationFactory operationFactory;
        private readonly IUserConfigManager userConfigManager;
        private readonly IScannedImageFactory scannedImageFactory;

        #endregion

        #region State Fields

        private readonly ScannedImageList imageList = new ScannedImageList();
        private bool isControlKeyDown;
        private CancellationTokenSource renderThumbnailsCts;
        private LayoutManager layoutManager;

        #endregion

        #region Initialization and Culture

        public FDesktop(IEmailer emailer, StringWrapper stringWrapper, AppConfigManager appConfigManager, RecoveryManager recoveryManager, IScannedImageImporter scannedImageImporter, AutoUpdaterUI autoUpdaterUI, OcrDependencyManager ocrDependencyManager, IProfileManager profileManager, IScanPerformer scanPerformer, IScannedImagePrinter scannedImagePrinter, ChangeTracker changeTracker, EmailSettingsContainer emailSettingsContainer, FileNamePlaceholders fileNamePlaceholders, ImageSettingsContainer imageSettingsContainer, PdfSettingsContainer pdfSettingsContainer, StillImage stillImage, IOperationFactory operationFactory, IUserConfigManager userConfigManager, IScannedImageFactory scannedImageFactory)
        {
            this.emailer = emailer;
            this.stringWrapper = stringWrapper;
            this.appConfigManager = appConfigManager;
            this.recoveryManager = recoveryManager;
            this.scannedImageImporter = scannedImageImporter;
            this.autoUpdaterUI = autoUpdaterUI;
            this.ocrDependencyManager = ocrDependencyManager;
            this.profileManager = profileManager;
            this.scanPerformer = scanPerformer;
            this.scannedImagePrinter = scannedImagePrinter;
            this.changeTracker = changeTracker;
            this.emailSettingsContainer = emailSettingsContainer;
            this.fileNamePlaceholders = fileNamePlaceholders;
            this.imageSettingsContainer = imageSettingsContainer;
            this.pdfSettingsContainer = pdfSettingsContainer;
            this.stillImage = stillImage;
            this.operationFactory = operationFactory;
            this.userConfigManager = userConfigManager;
            this.scannedImageFactory = scannedImageFactory;
            InitializeComponent();

            Shown += FDesktop_Shown;
            FormClosing += FDesktop_FormClosing;
            Closed += FDesktop_Closed;
        }

        protected override void OnLoad(object sender, EventArgs eventArgs)
        {
            PostInitializeComponent();
        }

        /// <summary>
        /// Runs when the form is first loaded and every time the language is changed.
        /// </summary>
        private void PostInitializeComponent()
        {
            imageList.UserConfigManager = UserConfigManager;
            thumbnailList1.UserConfigManager = UserConfigManager;
            int thumbnailSize = UserConfigManager.Config.ThumbnailSize;
            thumbnailList1.ThumbnailSize = new Size(thumbnailSize, thumbnailSize);

            RelayoutToolbar();
            InitLanguageDropdown();
            UpdateScanButton();
            LoadToolStripLocation();

            if (layoutManager != null)
            {
                layoutManager.Deactivate();
            }
            btnZoomIn.Location = new Point(btnZoomIn.Location.X, thumbnailList1.Height - 33);
            btnZoomOut.Location = new Point(btnZoomOut.Location.X, thumbnailList1.Height - 33);
            btnZoomMouseCatcher.Location = new Point(btnZoomMouseCatcher.Location.X, thumbnailList1.Height - 33);
            layoutManager = new LayoutManager(this)
                   .Bind(btnZoomIn, btnZoomOut, btnZoomMouseCatcher)
                       .BottomTo(() => thumbnailList1.Height)
                   .Activate();

            thumbnailList1.MouseWheel += thumbnailList1_MouseWheel;
            thumbnailList1.SizeChanged += (sender, args) => layoutManager.UpdateLayout();
        }

        private void InitLanguageDropdown()
        {
            // Read a list of languages from the Languages.resx file
            var resourceManager = LanguageResources.ResourceManager;
            var resourceSet = resourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
            foreach (DictionaryEntry entry in resourceSet.Cast<DictionaryEntry>().OrderBy(x => x.Value))
            {
                var langCode = ((string)entry.Key).Replace("_", "-");
                var langName = (string)entry.Value;

                // Only include those languages for which localized resources exist
                string localizedResourcesPath =
                    Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", langCode,
                        "NAPS2.Core.resources.dll");
                if (langCode == "en" || File.Exists(localizedResourcesPath))
                {
                    var button = new ToolStripMenuItem(langName, null, (sender, args) => SetCulture(langCode));
                    toolStripDropDownButton1.DropDownItems.Add(button);
                }
            }
        }

        private void RelayoutToolbar()
        {
            // Wrap text as necessary
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                btn.Text = stringWrapper.Wrap(btn.Text, 80, CreateGraphics(), btn.Font);
            }
            ResetToolbarMargin();
            // Recalculate visibility for the below check
            Application.DoEvents();
            // Check if toolbar buttons are overflowing
            if (tStrip.Items.OfType<ToolStripItem>().Any(btn => !btn.Visible))
            {
                ShrinkToolbarMargin();
            }
        }

        private void ResetToolbarMargin()
        {
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                if (btn is ToolStripSplitButton)
                {
                    btn.Margin = new Padding(5, 1, 5, 2);
                }
                else if (btn is ToolStripDoubleButton)
                {
                    btn.Padding = new Padding(5, 0, 5, 0);
                }
                else
                {
                    btn.Padding = new Padding(10, 0, 10, 0);
                }
            }
        }

        private void ShrinkToolbarMargin()
        {
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                if (btn is ToolStripSplitButton)
                {
                    btn.Margin = new Padding(0, 1, 0, 2);
                }
                else if (btn is ToolStripDoubleButton)
                {
                    btn.Padding = new Padding(0, 0, 0, 0);
                }
                else
                {
                    btn.Padding = new Padding(5, 0, 5, 0);
                }
            }
        }

        private void SetCulture(string cultureId)
        {
            SaveToolStripLocation();
            UserConfigManager.Config.Culture = cultureId;
            UserConfigManager.Save();
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureId);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureId);

            // Update localized values
            // Since all forms are opened modally and this is the root form, it should be the only one that needs to be updated live
            SaveFormState = false;
            Controls.RemoveAll();
            UpdateRTL();
            InitializeComponent();
            PostInitializeComponent();
            UpdateThumbnails();
            Focus();
            WindowState = FormWindowState.Normal;
            DoRestoreFormState();
            SaveFormState = true;
        }

        private void FDesktop_Shown(object sender, EventArgs e)
        {
            UpdateToolbar();

            // Receive messages from other processes
            Pipes.StartServer(msg =>
            {
                if (msg.StartsWith(Pipes.MSG_SCAN_WITH_DEVICE))
                {
                    Invoke(() => ScanWithDevice(msg.Substring(Pipes.MSG_SCAN_WITH_DEVICE.Length)));
                }
            });

            // If configured (e.g. by a business), show a customizable message box on application startup.
            var appConfig = appConfigManager.Config;
            if (!string.IsNullOrWhiteSpace(appConfig.StartupMessageText))
            {
                MessageBox.Show(appConfig.StartupMessageText, appConfig.StartupMessageTitle, MessageBoxButtons.OK,
                    appConfig.StartupMessageIcon);
            }

            // Allow scanned images to be recovered in case of an unexpected close
            recoveryManager.RecoverScannedImages(ReceiveScannedImage);

            // If NAPS2 was started by the scanner button, do the appropriate actions automatically
            RunStillImageEvents();

            // Automatic updates
            // Not yet enabled
            // autoUpdaterUI.OnApplicationStart(this);
        }

        #endregion

        #region Cleanup

        private void FDesktop_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (changeTracker.HasUnsavedChanges)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    var result = MessageBox.Show(MiscResources.ExitWithUnsavedChanges, MiscResources.UnsavedChanges,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (result == DialogResult.Yes)
                    {
                        changeTracker.HasUnsavedChanges = false;
                    }
                    else
                    {
                        e.Cancel = true;
                    }
                }
                else
                {
                    FileBasedScannedImage.DisableRecoveryCleanup = true;
                }
            }
        }

        private void FDesktop_Closed(object sender, EventArgs e)
        {
            SaveToolStripLocation();
            Pipes.KillServer();
            imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
        }

        #endregion

        #region Scanning and Still Image

        private void RunStillImageEvents()
        {
            if (stillImage.DoScan)
            {
                ScanWithDevice(stillImage.DeviceID);
            }
        }

        private void ScanWithDevice(string deviceID)
        {
            Activate();
            ScanProfile profile;
            if (profileManager.DefaultProfile != null && profileManager.DefaultProfile.Device != null
                && profileManager.DefaultProfile.Device.ID == deviceID)
            {
                // Try to use the default profile if it has the right device
                profile = profileManager.DefaultProfile;
            }
            else
            {
                // Otherwise just pick any old profile with the right device
                // Not sure if this is the best way to do it, but it's hard to prioritize profiles
                profile = profileManager.Profiles.FirstOrDefault(x => x.Device != null && x.Device.ID == deviceID);
            }
            if (profile == null)
            {
                // No profile for the device we're scanning with, so prompt to create one
                var editSettingsForm = FormFactory.Create<FEditScanSettings>();
                editSettingsForm.ScanProfile = appConfigManager.Config.DefaultProfileSettings ??
                                               new ScanProfile { Version = ScanProfile.CURRENT_VERSION };
                try
                {
                    // Populate the device field automatically (because we can do that!)
                    string deviceName = WiaApi.GetDeviceName(deviceID);
                    editSettingsForm.CurrentDevice = new ScanDevice(deviceID, deviceName);
                }
                catch (DeviceNotFoundException)
                {
                }
                editSettingsForm.ShowDialog();
                if (!editSettingsForm.Result)
                {
                    return;
                }
                profile = editSettingsForm.ScanProfile;
                profileManager.Profiles.Add(profile);
                profileManager.DefaultProfile = profile;
                profileManager.Save();

                UpdateScanButton();
            }
            if (profile != null)
            {
                // We got a profile, yay, so we can actually do the scan now
                scanPerformer.PerformScan(profile, new ScanParams(), this, ReceiveScannedImage);
                Activate();
            }
        }

        private void ScanDefault()
        {
            if (profileManager.DefaultProfile != null)
            {
                scanPerformer.PerformScan(profileManager.DefaultProfile, new ScanParams(), this, ReceiveScannedImage);
                Activate();
            }
            else if (profileManager.Profiles.Count == 0)
            {
                ScanWithNewProfile();
            }
            else
            {
                ShowProfilesForm();
            }
        }

        private void ScanWithNewProfile()
        {
            var editSettingsForm = FormFactory.Create<FEditScanSettings>();
            editSettingsForm.ScanProfile = appConfigManager.Config.DefaultProfileSettings ?? new ScanProfile { Version = ScanProfile.CURRENT_VERSION };
            editSettingsForm.ShowDialog();
            if (!editSettingsForm.Result)
            {
                return;
            }
            profileManager.Profiles.Add(editSettingsForm.ScanProfile);
            profileManager.DefaultProfile = editSettingsForm.ScanProfile;
            profileManager.Save();

            UpdateScanButton();

            scanPerformer.PerformScan(editSettingsForm.ScanProfile, new ScanParams(), this, ReceiveScannedImage);
            Activate();
        }

        #endregion

        #region Images and Thumbnails

        private IEnumerable<int> SelectedIndices
        {
            get
            {
                return thumbnailList1.SelectedIndices.Cast<int>();
            }
            set
            {
                thumbnailList1.SelectedIndices.Clear();
                foreach (int i in value)
                {
                    thumbnailList1.SelectedIndices.Add(i);
                }
            }
        }

        private IEnumerable<IScannedImage> SelectedImages
        {
            get { return imageList.Images.ElementsAt(SelectedIndices); }
        }

        public void ReceiveScannedImage(IScannedImage scannedImage)
        {
            Invoke(() =>
            {
                imageList.Images.Add(scannedImage);
                AppendThumbnail(scannedImage);
                changeTracker.HasUnsavedChanges = true;
                Application.DoEvents();
            });
        }

        private void UpdateThumbnails()
        {
            thumbnailList1.UpdateImages(imageList.Images);
            UpdateToolbar();
        }

        private void AppendThumbnail(IScannedImage scannedImage)
        {
            thumbnailList1.AppendImage(scannedImage);
            UpdateToolbar();
        }

        private void UpdateThumbnails(IEnumerable<int> selection)
        {
            UpdateThumbnails();
            SelectedIndices = selection;
        }

        #endregion

        #region Toolbar

        private void UpdateToolbar()
        {
            // "All" dropdown items
            tsSavePDFAll.Text = tsSaveImagesAll.Text = tsEmailPDFAll.Text = tsReverseAll.Text =
                string.Format(MiscResources.AllCount, imageList.Images.Count);
            tsSavePDFAll.Enabled = tsSaveImagesAll.Enabled = tsEmailPDFAll.Enabled = tsReverseAll.Enabled =
                imageList.Images.Any();

            // "Selected" dropdown items
            tsSavePDFSelected.Text = tsSaveImagesSelected.Text = tsEmailPDFSelected.Text = tsReverseSelected.Text =
                string.Format(MiscResources.SelectedCount, SelectedIndices.Count());
            tsSavePDFSelected.Enabled = tsSaveImagesSelected.Enabled = tsEmailPDFSelected.Enabled = tsReverseSelected.Enabled =
                SelectedIndices.Any();

            // Top-level toolbar actions
            tsdImage.Enabled = tsdRotate.Enabled = tsMove.Enabled = tsDelete.Enabled = SelectedIndices.Any();
            tsdReorder.Enabled = tsdSavePDF.Enabled = tsdSaveImages.Enabled = tsdEmailPDF.Enabled = tsdPrint.Enabled = tsClear.Enabled = imageList.Images.Any();

            // Context-menu actions
            ctxView.Visible = ctxCopy.Visible = ctxDelete.Visible = ctxSeparator1.Visible = ctxSeparator2.Visible = SelectedIndices.Any();
            ctxSelectAll.Enabled = imageList.Images.Any();

            // Other buttons
            btnZoomIn.Enabled = imageList.Images.Any() && UserConfigManager.Config.ThumbnailSize < ThumbnailHelper.MAX_SIZE;
            btnZoomOut.Enabled = imageList.Images.Any() && UserConfigManager.Config.ThumbnailSize > ThumbnailHelper.MIN_SIZE;
        }

        private void UpdateScanButton()
        {
            const int staticButtonCount = 2;

            // Clean up the dropdown
            while (tsScan.DropDownItems.Count > staticButtonCount)
            {
                tsScan.DropDownItems.RemoveAt(0);
            }

            // Populate the dropdown
            var defaultProfile = profileManager.DefaultProfile;
            foreach (var profile in profileManager.Profiles)
            {
                var item = new ToolStripMenuItem
                {
                    Text = profile.DisplayName.Replace("&", "&&"),
                    Image = profile == defaultProfile ? Icons.accept_small : null,
                    ImageScaling = ToolStripItemImageScaling.None
                };
                item.Click += (sender, args) =>
                {
                    profileManager.DefaultProfile = profile;
                    profileManager.Save();

                    UpdateScanButton();

                    scanPerformer.PerformScan(profile, new ScanParams(), this, ReceiveScannedImage);
                    Activate();
                };
                tsScan.DropDownItems.Insert(tsScan.DropDownItems.Count - staticButtonCount, item);
            }

            if (profileManager.Profiles.Any())
            {
                tsScan.DropDownItems.Insert(tsScan.DropDownItems.Count - staticButtonCount, new ToolStripSeparator());
            }
        }

        private void SaveToolStripLocation()
        {
            UserConfigManager.Config.DesktopToolStripDock = tStrip.Parent.Dock;
            UserConfigManager.Save();
        }

        private void LoadToolStripLocation()
        {
            var dock = UserConfigManager.Config.DesktopToolStripDock;
            if (dock != DockStyle.None)
            {
                var panel = toolStripContainer1.Controls.OfType<ToolStripPanel>().FirstOrDefault(x => x.Dock == dock);
                if (panel != null)
                {
                    tStrip.Parent = panel;
                }
            }
        }

        #endregion

        #region Actions

        private void Clear()
        {
            if (imageList.Images.Count > 0)
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmClearItems, imageList.Images.Count), MiscResources.Clear, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
                    UpdateThumbnails();
                    changeTracker.HasUnsavedChanges = false;
                }
            }
        }

        private void Delete()
        {
            if (SelectedIndices.Any())
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmDeleteItems, SelectedIndices.Count()), MiscResources.Delete, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    imageList.Delete(SelectedIndices);
                    UpdateThumbnails();
                    if (imageList.Images.Any())
                    {
                        changeTracker.HasUnsavedChanges = true;
                    }
                    else
                    {
                        changeTracker.HasUnsavedChanges = false;
                    }
                }
            }
        }

        private void SelectAll()
        {
            SelectedIndices = Enumerable.Range(0, imageList.Images.Count);
        }

        private void MoveDown()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.MoveDown(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        private void MoveUp()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.MoveUp(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        private void RotateLeft()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.RotateFlip(SelectedIndices, RotateFlipType.Rotate270FlipNone));
            changeTracker.HasUnsavedChanges = true;
        }

        private void RotateRight()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.RotateFlip(SelectedIndices, RotateFlipType.Rotate90FlipNone));
            changeTracker.HasUnsavedChanges = true;
        }

        private void Flip()
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            UpdateThumbnails(imageList.RotateFlip(SelectedIndices, RotateFlipType.RotateNoneFlipXY));
            changeTracker.HasUnsavedChanges = true;
        }

        private void PreviewImage()
        {
            if (SelectedIndices.Any())
            {
                using (var viewer = FormFactory.Create<FViewer>())
                {
                    viewer.ImageList = imageList;
                    viewer.ImageIndex = SelectedIndices.First();
                    viewer.DeleteCallback = UpdateThumbnails;
                    viewer.UpdateCallback = UpdateThumbnails;
                    viewer.ShowDialog();
                }
            }
        }

        private void ShowProfilesForm()
        {
            var form = FormFactory.Create<FProfiles>();
            form.ImageCallback = ReceiveScannedImage;
            form.ShowDialog();
            UpdateScanButton();
        }

        private void ResetImage()
        {
            if (SelectedIndices.Any())
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmResetImages, SelectedIndices.Count()), MiscResources.ResetImage, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    UpdateThumbnails(imageList.ResetTransforms(SelectedIndices));
                    changeTracker.HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region Actions - Save/Email/Import

        private void SavePDF(List<IScannedImage> images)
        {
            if (images.Any())
            {
                var sd = new SaveFileDialog
                {
                    OverwritePrompt = false,
                    AddExtension = true,
                    Filter = MiscResources.FileTypePdf + "|*.pdf",
                    FileName = pdfSettingsContainer.PdfSettings.DefaultFileName
                };

                if (sd.ShowDialog() == DialogResult.OK)
                {
                    ExportPDF(sd.FileName, images);
                    changeTracker.HasUnsavedChanges = false;
                }
            }
        }

        private void ExportPDF(string filename, List<IScannedImage> images)
        {
            var op = operationFactory.Create<SavePdfOperation>();
            var progressForm = FormFactory.Create<FProgress>();
            progressForm.Operation = op;

            var pdfSettings = pdfSettingsContainer.PdfSettings;
            pdfSettings.Metadata.Creator = MiscResources.NAPS2;
            var ocrLanguageCode = userConfigManager.Config.EnableOcr ? userConfigManager.Config.OcrLanguageCode : null;
            if (op.Start(filename, DateTime.Now, images, pdfSettings, ocrLanguageCode))
            {
                progressForm.ShowDialog();
            }
        }

        private void SaveImages(List<IScannedImage> images)
        {
            if (images.Any())
            {
                var sd = new SaveFileDialog
                {
                    OverwritePrompt = false,
                    AddExtension = true,
                    Filter = MiscResources.FileTypeBmp + "|*.bmp|" +
                                MiscResources.FileTypeEmf + "|*.emf|" +
                                MiscResources.FileTypeExif + "|*.exif|" +
                                MiscResources.FileTypeGif + "|*.gif|" +
                                MiscResources.FileTypeJpeg + "|*.jpg;*.jpeg|" +
                                MiscResources.FileTypePng + "|*.png|" +
                                MiscResources.FileTypeTiff + "|*.tiff;*.tif",
                    FileName = imageSettingsContainer.ImageSettings.DefaultFileName
                };
                switch ((UserConfigManager.Config.LastImageExt ?? "").ToLowerInvariant())
                {
                    case "bmp":
                        sd.FilterIndex = 1;
                        break;
                    case "emf":
                        sd.FilterIndex = 2;
                        break;
                    case "exif":
                        sd.FilterIndex = 3;
                        break;
                    case "gif":
                        sd.FilterIndex = 4;
                        break;
                    case "png":
                        sd.FilterIndex = 6;
                        break;
                    case "tif":
                    case "tiff":
                        sd.FilterIndex = 7;
                        break;
                    default: // Jpeg
                        sd.FilterIndex = 5;
                        break;
                }

                if (sd.ShowDialog() == DialogResult.OK)
                {
                    UserConfigManager.Config.LastImageExt = (Path.GetExtension(sd.FileName) ?? "").Replace(".", "");
                    UserConfigManager.Save();

                    var op = operationFactory.Create<SaveImagesOperation>();
                    var progressForm = FormFactory.Create<FProgress>();
                    progressForm.Operation = op;
                    if (op.Start(sd.FileName, DateTime.Now, images))
                    {
                        progressForm.ShowDialog();
                    }

                    changeTracker.HasUnsavedChanges = false;
                }
            }
        }

        private void EmailPDF(List<IScannedImage> images)
        {
            if (images.Any())
            {
                var emailSettings = emailSettingsContainer.EmailSettings;
                var invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
                var attachmentName = new string(emailSettings.AttachmentName.Where(x => !invalidChars.Contains(x)).ToArray());
                if (string.IsNullOrEmpty(attachmentName))
                {
                    attachmentName = "Scan.pdf";
                }
                if (!attachmentName.EndsWith(".pdf", StringComparison.InvariantCultureIgnoreCase))
                {
                    attachmentName += ".pdf";
                }
                attachmentName = fileNamePlaceholders.SubstitutePlaceholders(attachmentName, DateTime.Now, false);

                var tempFolder = new DirectoryInfo(Path.Combine(Paths.Temp, Path.GetRandomFileName()));
                tempFolder.Create();
                try
                {
                    string targetPath = Path.Combine(tempFolder.FullName, attachmentName);
                    ExportPDF(targetPath, images);
                    var message = new EmailMessage
                    {
                        Attachments =
                        {
                            new EmailAttachment
                            {
                                FilePath = targetPath,
                                AttachmentName = attachmentName
                            }
                        }
                    };

                    if (emailer.SendEmail(message))
                    {
                        changeTracker.HasUnsavedChanges = false;
                    }
                }
                finally
                {
                    tempFolder.Delete(true);
                }
            }
        }

        private void Import()
        {
            var ofd = new OpenFileDialog
            {
                Multiselect = true,
                CheckFileExists = true
            };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                ImportFiles(ofd.FileNames);
            }
        }

        private void ImportFiles(IEnumerable<string> files)
        {
            var op = operationFactory.Create<ImportOperation>();
            var progressForm = FormFactory.Create<FProgress>();
            progressForm.Operation = op;
            if (op.Start(files.OrderBy(x => x).ToList(), ReceiveScannedImage))
            {
                progressForm.ShowDialog();
            }
        }

        private void ImportDirect(DirectImageTransfer data, bool copy)
        {
            var op = operationFactory.Create<DirectImportOperation>();
            var progressForm = FormFactory.Create<FProgress>();
            progressForm.Operation = op;
            if (op.Start(data, copy, ReceiveScannedImage))
            {
                progressForm.ShowDialog();
            }
        }

        #endregion

        #region Keyboard Shortcuts

        private void thumbnailList1_KeyDown(object sender, KeyEventArgs e)
        {
            isControlKeyDown = e.Control;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Up:
                    if (e.Control)
                    {
                        MoveUp();
                    }
                    break;
                case Keys.Right:
                case Keys.Down:
                    if (e.Control)
                    {
                        MoveDown();
                    }
                    break;
                case Keys.O:
                    if (e.Control)
                    {
                        Import();
                    }
                    break;
                case Keys.Enter:
                    if (e.Control)
                    {
                        ScanDefault();
                    }
                    break;
                case Keys.S:
                    if (e.Control)
                    {
                        SavePDF(imageList.Images);
                    }
                    break;
                case Keys.OemMinus:
                    if (e.Control)
                    {
                        StepThumbnailSize(-1);
                    }
                    break;
                case Keys.Oemplus:
                    if (e.Control)
                    {
                        StepThumbnailSize(1);
                    }
                    break;
            }
        }

        private void thumbnailList1_KeyUp(object sender, KeyEventArgs e)
        {
            isControlKeyDown = e.Control;
        }

        private void thumbnailList1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (isControlKeyDown)
            {
                StepThumbnailSize(e.Delta / (double)SystemInformation.MouseWheelScrollDelta);
            }
        }

        #endregion

        #region Event Handlers - Misc

        private void thumbnailList1_ItemActivate(object sender, EventArgs e)
        {
            PreviewImage();
        }

        private void thumbnailList1_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateToolbar();
        }

        private void thumbnailList1_MouseMove(object sender, MouseEventArgs e)
        {
            Cursor = thumbnailList1.GetItemAt(e.X, e.Y) == null ? Cursors.Default : Cursors.Hand;
        }

        private void thumbnailList1_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        #endregion

        #region Event Handlers - Toolbar

        private void tsScan_ButtonClick(object sender, EventArgs e)
        {
            ScanDefault();
        }

        private void tsNewProfile_Click(object sender, EventArgs e)
        {
            ScanWithNewProfile();
        }

        private void tsBatchScan_Click(object sender, EventArgs e)
        {
            var form = FormFactory.Create<FBatchScan>();
            form.ImageCallback = ReceiveScannedImage;
            form.ShowDialog();
            UpdateScanButton();
        }

        private void tsProfiles_Click(object sender, EventArgs e)
        {
            ShowProfilesForm();
        }

        private void tsOcr_Click(object sender, EventArgs e)
        {
            if (ocrDependencyManager.IsExecutableDownloaded && ocrDependencyManager.GetDownloadedLanguages().Any())
            {
                FormFactory.Create<FOcrSetup>().ShowDialog();
            }
            else
            {
                FormFactory.Create<FOcrLanguageDownload>().ShowDialog();
                if (ocrDependencyManager.IsExecutableDownloaded && ocrDependencyManager.GetDownloadedLanguages().Any())
                {
                    FormFactory.Create<FOcrSetup>().ShowDialog();
                }
            }
        }

        private void tsImport_Click(object sender, EventArgs e)
        {
            Import();
        }

        private void tsdSavePDF_ButtonClick(object sender, EventArgs e)
        {
            if (appConfigManager.Config.SaveButtonDefaultAction == SaveButtonDefaultAction.AlwaysPrompt)
            {
                tsdSavePDF.ShowDropDown();
            }
            else if (appConfigManager.Config.SaveButtonDefaultAction == SaveButtonDefaultAction.SaveSelected && SelectedIndices.Any())
            {
                SavePDF(SelectedImages.ToList());
            }
            else
            {
                SavePDF(imageList.Images);
            }
        }

        private void tsdSaveImages_ButtonClick(object sender, EventArgs e)
        {
            if (appConfigManager.Config.SaveButtonDefaultAction == SaveButtonDefaultAction.AlwaysPrompt)
            {
                tsdSaveImages.ShowDropDown();
            }
            else if (appConfigManager.Config.SaveButtonDefaultAction == SaveButtonDefaultAction.SaveSelected && SelectedIndices.Any())
            {
                SaveImages(SelectedImages.ToList());
            }
            else
            {
                SaveImages(imageList.Images);
            }
        }

        private void tsdEmailPDF_ButtonClick(object sender, EventArgs e)
        {
            if (appConfigManager.Config.SaveButtonDefaultAction == SaveButtonDefaultAction.AlwaysPrompt)
            {
                tsdEmailPDF.ShowDropDown();
            }
            else if (appConfigManager.Config.SaveButtonDefaultAction == SaveButtonDefaultAction.SaveSelected && SelectedIndices.Any())
            {
                EmailPDF(SelectedImages.ToList());
            }
            else
            {
                EmailPDF(imageList.Images);
            }
        }

        private void tsdPrint_Click(object sender, EventArgs e)
        {
            if (scannedImagePrinter.PromptToPrint(imageList.Images, SelectedImages.ToList()))
            {
                changeTracker.HasUnsavedChanges = false;
            }
        }

        private void tsMove_ClickFirst(object sender, EventArgs e)
        {
            MoveUp();
        }

        private void tsMove_ClickSecond(object sender, EventArgs e)
        {
            MoveDown();
        }

        private void tsDelete_Click(object sender, EventArgs e)
        {
            Delete();
        }

        private void tsClear_Click(object sender, EventArgs e)
        {
            Clear();
        }

        private void tsAbout_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FAbout>().ShowDialog();
        }

        #endregion

        #region Event Handlers - Save/Email Menus

        private void tsSavePDFAll_Click(object sender, EventArgs e)
        {
            SavePDF(imageList.Images);
        }

        private void tsSavePDFSelected_Click(object sender, EventArgs e)
        {
            SavePDF(SelectedImages.ToList());
        }

        private void tsPDFSettings_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FPdfSettings>().ShowDialog();
        }

        private void tsSaveImagesAll_Click(object sender, EventArgs e)
        {
            SaveImages(imageList.Images);
        }

        private void tsSaveImagesSelected_Click(object sender, EventArgs e)
        {
            SaveImages(SelectedImages.ToList());
        }

        private void tsImageSettings_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FImageSettings>().ShowDialog();
        }

        private void tsEmailPDFAll_Click(object sender, EventArgs e)
        {
            EmailPDF(imageList.Images);
        }

        private void tsEmailPDFSelected_Click(object sender, EventArgs e)
        {
            EmailPDF(SelectedImages.ToList());
        }

        private void tsPdfSettings2_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FPdfSettings>().ShowDialog();
        }

        private void tsEmailSettings_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FEmailSettings>().ShowDialog();
        }

        #endregion

        #region Event Handlers - Image Menu

        private void tsView_Click(object sender, EventArgs e)
        {
            PreviewImage();
        }

        private void tsCrop_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FCrop>();
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateThumbnails(SelectedIndices.ToList());
            }
        }

        private void tsBrightness_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FBrightness>();
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateThumbnails(SelectedIndices.ToList());
            }
        }

        private void tsContrast_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FContrast>();
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateThumbnails(SelectedIndices.ToList());
            }
        }

        private void tsReset_Click(object sender, EventArgs e)
        {
            ResetImage();
        }

        #endregion

        #region Event Handlers - Rotate Menu

        private void tsRotateLeft_Click(object sender, EventArgs e)
        {
            RotateLeft();
        }

        private void tsRotateRight_Click(object sender, EventArgs e)
        {
            RotateRight();
        }

        private void tsFlip_Click(object sender, EventArgs e)
        {
            Flip();
        }

        private void customRotationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                var form = FormFactory.Create<FRotate>();
                form.Image = SelectedImages.First();
                form.SelectedImages = SelectedImages.ToList();
                form.ShowDialog();
                UpdateThumbnails(SelectedIndices.ToList());
            }
        }

        #endregion

        #region Event Handlers - Reorder Menu

        private void tsInterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.Interleave(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        private void tsDeinterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.Deinterleave(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        private void tsAltInterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.AltInterleave(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        private void tsAltDeinterleave_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 3)
            {
                return;
            }
            UpdateThumbnails(imageList.AltDeinterleave(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        private void tsReverseAll_Click(object sender, EventArgs e)
        {
            if (imageList.Images.Count < 2)
            {
                return;
            }
            UpdateThumbnails(imageList.Reverse());
            changeTracker.HasUnsavedChanges = true;
        }

        private void tsReverseSelected_Click(object sender, EventArgs e)
        {
            if (SelectedIndices.Count() < 2)
            {
                return;
            }
            UpdateThumbnails(imageList.Reverse(SelectedIndices));
            changeTracker.HasUnsavedChanges = true;
        }

        #endregion

        #region Auto Update

        public void UpdateAvailable(VersionInfo versionInfo)
        {
            Invoke(() => autoUpdaterUI.PerformUpdate(this, versionInfo));
        }

        public void InstallComplete()
        {
            Invoke(() =>
            {
                switch (MessageBox.Show(MiscResources.InstallCompletePromptRestart, MiscResources.InstallComplete, MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        Close(); // TODO: This close might be canceled. Handle that.
                        Process.Start(Application.ExecutablePath);
                        break;
                }
            });
        }

        #endregion

        #region Context Menu

        private void contextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ctxPaste.Enabled = CanPaste;
            if (!imageList.Images.Any() && !ctxPaste.Enabled)
            {
                e.Cancel = true;
            }
        }

        private void ctxSelectAll_Click(object sender, EventArgs e)
        {
            SelectAll();
        }

        private void ctxView_Click(object sender, EventArgs e)
        {
            PreviewImage();
        }

        private void ctxCopy_Click(object sender, EventArgs e)
        {
            CopyImages();
        }

        private void ctxPaste_Click(object sender, EventArgs e)
        {
            PasteImages();
        }

        private void ctxDelete_Click(object sender, EventArgs e)
        {
            Delete();
        }

        #endregion

        #region Clipboard

        private void CopyImages()
        {
            if (SelectedIndices.Any())
            {
                var ido = GetDataObjectForImages(SelectedImages, true);
                Clipboard.SetDataObject(ido);
            }
        }

        private void PasteImages()
        {
            var ido = Clipboard.GetDataObject();
            if (ido == null)
            {
                return;
            }
            if (ido.GetDataPresent(typeof(DirectImageTransfer).FullName))
            {
                var data = (DirectImageTransfer)ido.GetData(typeof(DirectImageTransfer).FullName);
                ImportDirect(data, true);
            }
        }

        private bool CanPaste
        {
            get
            {
                var ido = Clipboard.GetDataObject();
                return ido != null && ido.GetDataPresent(typeof (DirectImageTransfer).FullName);
            }
        }

        private static IDataObject GetDataObjectForImages(IEnumerable<IScannedImage> images, bool includeBitmap)
        {
            var imageList = images.ToList();
            IDataObject ido = new DataObject();
            if (imageList.Count == 0)
            {
                return ido;
            }
            if (includeBitmap)
            {
                var firstBitmap = imageList[0].GetImage();
                ido.SetData(DataFormats.Bitmap, true, firstBitmap);
                const int maxRtfSize = 20 * 1000 * 1000;
                var rtfEncodedImages = new StringBuilder();
                rtfEncodedImages.Append("{");
                rtfEncodedImages.Append(GetRtfEncodedImage(firstBitmap, imageList[0].FileFormat));
                foreach (var img in imageList.Skip(1))
                {
                    if (rtfEncodedImages.Length > maxRtfSize)
                    {
                        break;
                    }
                    var bitmap = img.GetImage();
                    rtfEncodedImages.Append(@"\par");
                    rtfEncodedImages.Append(GetRtfEncodedImage(bitmap, img.FileFormat));
                    bitmap.Dispose();
                }
                rtfEncodedImages.Append("}");
                ido.SetData(DataFormats.Rtf, true, rtfEncodedImages.ToString());
            }
            ido.SetData(typeof(DirectImageTransfer), new DirectImageTransfer(imageList));
            return ido;
        }

        private static string GetRtfEncodedImage(Image image, ImageFormat format)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, format);
                string hexString = BitConverter.ToString(stream.ToArray(), 0).Replace("-", string.Empty);

                return @"{\pict\pngblip\picw" +
                       image.Width + @"\pich" + image.Height +
                       @"\picwgoa" + image.Width + @"\pichgoa" + image.Height +
                       @"\hex " + hexString + "}";
            }
        }

        #endregion

        #region Thumbnail Resizing

        private void StepThumbnailSize(double step)
        {
            int thumbnailSize = UserConfigManager.Config.ThumbnailSize;
            thumbnailSize += (int)(ThumbnailHelper.STEP_SIZE * step);
            thumbnailSize = Math.Max(Math.Min(thumbnailSize, ThumbnailHelper.MAX_SIZE), ThumbnailHelper.MIN_SIZE);
            ResizeThumbnails(thumbnailSize);
        }

        private void ResizeThumbnails(int thumbnailSize)
        {
            if (!imageList.Images.Any())
            {
                // Can't show visual feedback so don't do anything
                return;
            }

            // Save the new size to config
            UserConfigManager.Config.ThumbnailSize = thumbnailSize;
            UserConfigManager.Save();
            UpdateToolbar();
            // Adjust the visible thumbnail display with the new size
            thumbnailList1.ThumbnailSize = new Size(thumbnailSize, thumbnailSize);
            thumbnailList1.RegenerateThumbnailList(imageList.Images);

            // Render high-quality thumbnails at the new size in a background task
            // The existing (poorly scaled) thumbnails are used in the meantime
            RenderThumbnails(thumbnailSize, imageList.Images.ToList());
        }

        private void RenderThumbnails(int thumbnailSize, IEnumerable<IScannedImage> imagesToRenderThumbnailsFor)
        {
            if (renderThumbnailsCts != null)
            {
                // Cancel any previous task so that no two run at the same time
                renderThumbnailsCts.Cancel();
            }
            renderThumbnailsCts = new CancellationTokenSource();
            var ct = renderThumbnailsCts.Token;
            Task.Factory.StartNew(() =>
            {
                foreach (var img in imagesToRenderThumbnailsFor)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    // Save the state to check later for concurrent changes
                    var oldState = img.GetThumbnailState();
                    // Render the thumbnail
                    Bitmap thumbnail;
                    try
                    {
                        thumbnail = img.RenderThumbnail(thumbnailSize);
                    }
                    catch
                    {
                        // An error occurred, which could mean the image was deleted
                        // In any case we don't need to worry too much about it and can move on to the next
                        continue;
                    }
                    // Do the rest of the stuff on the UI thread to help with synchronization
                    Invoke(() =>
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        // Check for concurrent transformations
                        if (oldState != img.GetThumbnailState())
                        {
                            // The thumbnail has been concurrently updated
                            return;
                        }
                        // Checks passed, so use the newly rendered thumbnail at the appropriate index
                        img.SetThumbnail(thumbnail);
                        int index = imageList.Images.IndexOf(img);
                        if (index != -1)
                        {
                            thumbnailList1.ReplaceThumbnail(index, thumbnail);
                        }
                    });
                }
            }, ct);
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            StepThumbnailSize(-1);
        }

        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            StepThumbnailSize(1);
        }

        #endregion

        #region Drag/Drop

        private void thumbnailList1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Provide drag data
            if (SelectedIndices.Any())
            {
                var ido = GetDataObjectForImages(SelectedImages, false);
                DoDragDrop(ido, DragDropEffects.Move | DragDropEffects.Copy);
            }
        }

        private void thumbnailList1_DragEnter(object sender, DragEventArgs e)
        {
            // Determine if drop data is compatible
            try
            {
                if (e.Data.GetDataPresent(typeof(DirectImageTransfer).FullName))
                {
                    var data = (DirectImageTransfer)e.Data.GetData(typeof(DirectImageTransfer).FullName);
                    e.Effect = data.ProcessID == Process.GetCurrentProcess().Id
                        ? DragDropEffects.Move
                        : DragDropEffects.Copy;
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error receiving drag/drop", ex);
            }
        }

        private void thumbnailList1_DragDrop(object sender, DragEventArgs e)
        {
            // Receive drop data
            if (e.Data.GetDataPresent(typeof(DirectImageTransfer).FullName))
            {
                var data = (DirectImageTransfer)e.Data.GetData(typeof(DirectImageTransfer).FullName);
                if (data.ProcessID == Process.GetCurrentProcess().Id)
                {
                    DragMoveImages(e);
                }
                else
                {
                    ImportDirect(data, false);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var data = (string[])e.Data.GetData(DataFormats.FileDrop);
                ImportFiles(data);
            }
        }

        private void DragMoveImages(DragEventArgs e)
        {
            if (!SelectedIndices.Any())
            {
                return;
            }
            Point cp = thumbnailList1.PointToClient(new Point(e.X, e.Y));
            ListViewItem dragToItem = thumbnailList1.GetItemAt(cp.X, cp.Y);
            if (dragToItem == null)
            {
                var items = thumbnailList1.Items.Cast<ListViewItem>().ToList();
                var minY = items.Select(x => x.Bounds.Top).Min();
                var maxY = items.Select(x => x.Bounds.Bottom).Max();
                if (cp.Y < minY)
                {
                    UpdateThumbnails(imageList.MoveTo(SelectedIndices, 0));
                    changeTracker.HasUnsavedChanges = true;
                }
                else if (cp.Y > maxY)
                {
                    UpdateThumbnails(imageList.MoveTo(SelectedIndices, imageList.Images.Count));
                    changeTracker.HasUnsavedChanges = true;
                }
                else
                {
                    var row =
                        items.Where(x => x.Bounds.Top <= cp.Y && x.Bounds.Bottom >= cp.Y).OrderBy(x => x.Bounds.X).ToList();
                    dragToItem = row.FirstOrDefault(x => x.Bounds.Right >= cp.X) ?? row.LastOrDefault();
                }
            }
            if (dragToItem == null)
            {
                return;
            }
            int dragToIndex = dragToItem.ImageIndex;
            if (cp.X > (dragToItem.Bounds.X + dragToItem.Bounds.Width / 2))
            {
                dragToIndex++;
            }
            UpdateThumbnails(imageList.MoveTo(SelectedIndices, dragToIndex));
            changeTracker.HasUnsavedChanges = true;
        }

        #endregion
    }
}
