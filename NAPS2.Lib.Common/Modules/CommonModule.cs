﻿using System;
using System.Collections.Generic;
using System.Linq;
using NAPS2.ClientServer;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.ImportExport.Images;
using NAPS2.ImportExport.Pdf;
using NAPS2.Ocr;
using NAPS2.Operation;
using NAPS2.Scan;
using NAPS2.Images;
using NAPS2.ImportExport.Email.Mapi;
using NAPS2.Scan.Sane;
using NAPS2.Scan.Twain;
using NAPS2.Scan.Wia;
using NAPS2.Util;
using NAPS2.WinForms;
using NAPS2.Worker;
using Ninject;
using Ninject.Modules;
using ILogger = NAPS2.Logging.ILogger;

namespace NAPS2.DI.Modules
{
    public class CommonModule : NinjectModule
    {
        public override void Load()
        {
            // Import
            Bind<IScannedImageImporter>().To<ScannedImageImporter>();
            Bind<IPdfImporter>().To<PdfSharpImporter>();
            Bind<IImageImporter>().To<ImageImporter>();
            Bind<IPdfRenderer>().To<GhostscriptPdfRenderer>().InSingletonScope();

            // Export
            Bind<PdfExporter>().To<PdfSharpExporter>();
            Bind<IScannedImagePrinter>().To<PrintDocumentPrinter>();
            Bind<IEmailProviderFactory>().To<NinjectEmailProviderFactory>();
            Bind<IMapiWrapper>().To<MapiWrapper>();
            Bind<OcrEngineManager>().ToMethod(ctx => OcrEngineManager.Default);
            Bind<OcrRequestQueue>().ToSelf().InSingletonScope();

            // Scan
            Bind<IScanPerformer>().To<ScanPerformer>();
#if DEBUG && false
            Bind<IScanDriverFactory>().To<Scan.Stub.StubScanDriverFactory>();
#else
            Bind<IScanDriverFactory>().To<NinjectScanDriverFactory>();
#endif
            Bind<IScanDriver>().To<WiaScanDriver>().InSingletonScope().Named(WiaScanDriver.DRIVER_NAME);
            Bind<IScanDriver>().To<TwainScanDriver>().InSingletonScope().Named(TwainScanDriver.DRIVER_NAME);
            Bind<IScanDriver>().To<SaneScanDriver>().InSingletonScope().Named(SaneScanDriver.DRIVER_NAME);
            Bind<IScanDriver>().To<ProxiedScanDriver>().InSingletonScope().Named(ProxiedScanDriver.DRIVER_NAME);
            Bind<ITwainWrapper>().To<TwainWrapper>();

            // Config
            Bind<PdfSettingsContainer>().ToSelf().InSingletonScope();
            Bind<PdfSettingsProvider>().ToMethod(ctx => ctx.Kernel.Get<PdfSettingsContainer>());
            Bind<ImageSettingsContainer>().ToSelf().InSingletonScope();
            Bind<ImageSettingsProvider>().ToMethod(ctx => ctx.Kernel.Get<ImageSettingsContainer>());
            Bind<EmailSettingsContainer>().ToSelf().InSingletonScope();

            // Host
            Bind<IWorkerServiceFactory>().ToMethod(ctx => WorkerManager.Factory);

            // Misc
            Bind<IFormFactory>().To<NinjectFormFactory>();
            Bind<NotificationManager>().ToSelf().InSingletonScope();
            Bind<IOperationFactory>().To<NinjectOperationFactory>();
            Bind<ILogger>().To<NLogLogger>().InSingletonScope();
            Bind<ChangeTracker>().ToSelf().InSingletonScope();
            Bind<StillImage>().ToSelf().InSingletonScope();
            Bind<BlankDetector>().To<ThresholdBlankDetector>();
            Bind<AutoSaver>().ToSelf();

            StaticConfiguration.Initialize();
        }
    }
}
