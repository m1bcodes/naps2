using System.Threading;
using NAPS2.Remoting.Worker;
using NAPS2.Scan;

namespace NAPS2.Images;

// TODO: Write a bunch of tests for this class
public class ThumbnailRenderQueue : IDisposable
{
    private readonly ScanningContext _scanningContext;
    private readonly ThumbnailRenderer _thumbnailRenderer;
    private readonly AutoResetEvent _renderThumbnailsWaitHandle = new(false);
    private int _thumbnailSize;
    private UiImageList? _imageList;
    private bool _started;
    private bool _disposed;

    public ThumbnailRenderQueue(ScanningContext scanningContext, ThumbnailRenderer thumbnailRenderer)
    {
        _scanningContext = scanningContext;
        _thumbnailRenderer = thumbnailRenderer;
    }

    public void SetThumbnailSize(int thumbnailSize)
    {
        lock (this)
        {
            _thumbnailSize = thumbnailSize;
        }
        _renderThumbnailsWaitHandle.Set();
    }

    public void StartRendering(UiImageList imageList)
    {
        lock (this)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ThumbnailRenderQueue));
            if (_started) throw new InvalidOperationException();
            _imageList = imageList;
            _started = true;
        }
        lock (_imageList)
        {
            _imageList.ImagesUpdated += ImageListUpdated;
            _imageList.ImagesThumbnailInvalidated += ImageListUpdated;
        }
        new Thread(RenderThumbnails).Start();
    }

    // TODO: Maybe try and avoid the need for explicit bumps
    public void Bump()
    {
        _renderThumbnailsWaitHandle.Set();
    }

    private void ImageListUpdated(object? sender, EventArgs args)
    {
        _renderThumbnailsWaitHandle.Set();
    }

    private void RenderThumbnails()
    {
        // TODO: Make this run as async?
        bool useWorker = PlatformCompat.Runtime.UseWorker;
        var worker = useWorker ? _scanningContext.WorkerFactory.Create() : null;
        var fallback = new ExpFallback(100, 60 * 1000);
        while (true)
        {
            int thumbnailSize;
            lock (this)
            {
                if (_disposed) break;
                thumbnailSize = _thumbnailSize;
            }
            try
            {
                UiImage? next;
                // TODO: What if thumbnail size updates in the middle here?
                while ((next = GetNextThumbnailToRender(thumbnailSize)) != null)
                {
                    if (!ThumbnailStillNeedsRendering(next, thumbnailSize))
                    {
                        continue;
                    }
                    using (var imageToRender = next.GetClonedImage())
                    {
                        var thumb = worker != null
                            ? RenderThumbnailWithWorker(worker, imageToRender, thumbnailSize)
                            : _thumbnailRenderer.Render(imageToRender, thumbnailSize);

                        if (!ThumbnailStillNeedsRendering(next, thumbnailSize))
                        {
                            continue;
                        }

                        next.SetThumbnail(thumb, imageToRender.TransformState);
                    }
                    fallback.Reset();
                }
            }
            catch (Exception e)
            {
                Log.ErrorException("Error rendering thumbnails", e);
                if (worker != null)
                {
                    worker.Dispose();
                    worker = _scanningContext.WorkerFactory.Create();
                }
                Thread.Sleep(fallback.Value);
                fallback.Increase();
                continue;
            }
            _renderThumbnailsWaitHandle.WaitOne();
        }
    }

    private IMemoryImage RenderThumbnailWithWorker(WorkerContext worker, ProcessedImage imageToRender,
        int thumbnailSize)
    {
        var buffer = worker.Service.RenderThumbnail(_scanningContext.ImageContext, imageToRender, thumbnailSize);
        return _scanningContext.ImageContext.Load(new MemoryStream(buffer));
    }

    private bool ThumbnailStillNeedsRendering(UiImage next, int thumbnailSize)
    {
        lock (next)
        {
            var thumb = next.GetThumbnailClone();
            return thumb == null || next.IsThumbnailDirty || thumb.Width != thumbnailSize ||
                   thumb.Height != thumbnailSize;
        }
    }

    private UiImage? GetNextThumbnailToRender(int thumbnailSize)
    {
        List<UiImage> listCopy;
        lock (_imageList)
        {
            listCopy = _imageList.Images.ToList();
        }
        // TODO: Lock the images?
        // TODO: Also double check this logic in general 
        // Look for images without thumbnails
        foreach (var img in listCopy)
        {
            if (img.GetThumbnailClone() == null)
            {
                return img;
            }
        }
        // Look for images with dirty thumbnails
        foreach (var img in listCopy)
        {
            if (img.IsThumbnailDirty)
            {
                return img;
            }
        }
        // Look for images with mis-sized thumbnails
        foreach (var img in listCopy)
        {
            var thumb = img.GetThumbnailClone();
            if (thumb == null || thumb.Width != thumbnailSize || thumb.Height != thumbnailSize)
            {
                return img;
            }
        }
        // Nothing to render
        return null;
    }

    public void Dispose()
    {
        lock (this)
        {
            if (_disposed) return;
            _disposed = true;
            if (_imageList != null)
            {
                lock (_imageList)
                {
                    _imageList.ImagesUpdated -= ImageListUpdated;
                    _imageList.ImagesThumbnailInvalidated -= ImageListUpdated;
                }
            }
        }
    }
}