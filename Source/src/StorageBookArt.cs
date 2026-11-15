using Sons.Crafting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LongStickSpear;

internal static class StorageBookArt
{
    private sealed record PendingPage(BlueprintBookPageData Page, Texture2D Source, bool SourceIsTop);
    private static readonly List<PendingPage> Pending = new();
    private static Texture2D? _image, _requestedSource;
    private static IntPtr _imageSource;
    private static bool _imageIsTop, _reportedWait;
    private static int _previousRequest;
    private static float _nextPoll, _requestedAt;

    // Keep the native texture/recipe slot usable while its detailed mip streams
    // in. Only replace the texture; book interaction slots never move at runtime.
    internal static void Request(BlueprintBookPageData page, Texture2D source, bool sourceIsTop)
    {
        if (_image != null && _image && _imageSource == source.Pointer && _imageIsTop == sourceIsTop)
        { page._pageImage = _image; return; }
        if (!Pending.Any(entry => entry.Page.Pointer == page.Pointer)) Pending.Add(new(page, source, sourceIsTop));
    }

    internal static void Update()
    {
        if (Pending.Count == 0 || !Mod.RuntimeReady || Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + .25f;
        var entry = Pending[0];
        try
        {
            var source = entry.Source;
            if (source == null || !source) { Pending.RemoveAt(0); Pause(); return; }
            if (_requestedSource?.Pointer != source.Pointer)
            {
                Pause(); _requestedSource = source; _requestedAt = Time.unscaledTime;
                if (source.streamingMipmaps)
                {
                    _previousRequest = source.requestedMipmapLevel;
                    source.requestedMipmapLevel = 0;
                    _nextPoll = Time.unscaledTime + .25f;
                    return; // Never copy during the frame that requests the mip.
                }
            }
            if (source.streamingMipmaps)
            {
                source.requestedMipmapLevel = 0;
                if (!source.IsRequestedMipmapLevelLoaded() || source.loadedMipmapLevel > source.activeMipmapLimit)
                {
                    if (!_reportedWait && Time.unscaledTime - _requestedAt >= 15f)
                    { _reportedWait = true; Mod.Info("BOOK ART: waiting for the detailed source mip; the native page remains usable."); }
                    _nextPoll = Time.unscaledTime + (_reportedWait ? 1f : .25f);
                    return;
                }
            }
            var image = Create(source, entry.SourceIsTop);
            _image = image; _imageSource = source.Pointer; _imageIsTop = entry.SourceIsTop;
            for (var i = Pending.Count - 1; i >= 0; i--)
                if (Pending[i].Source.Pointer == source.Pointer && Pending[i].SourceIsTop == entry.SourceIsTop)
                { Pending[i].Page._pageImage = image; Pending.RemoveAt(i); }
            Mod.Info($"BOOK ART: isolated holder page {image.width}x{image.height}, sourceMip={(source.streamingMipmaps ? source.loadedMipmapLevel : 0)}, qualityLimit={source.activeMipmapLimit}, trilinear/aniso={image.anisoLevel}.");
            Pause();
        }
        catch (Exception ex)
        {
            Mod.Warn("Could not prepare the detailed holder book image; keeping the native page. " + ex.Message);
            for (var i = Pending.Count - 1; i >= 0; i--)
                if (Pending[i].Source.Pointer == entry.Source.Pointer) Pending.RemoveAt(i);
            Pause();
        }
    }

    internal static void Pause()
    {
        try
        {
            if (_requestedSource != null && _requestedSource && _requestedSource.streamingMipmaps && _requestedSource.requestedMipmapLevel == 0)
            {
                if (_previousRequest < 0) _requestedSource.ClearRequestedMipmapLevel();
                else _requestedSource.requestedMipmapLevel = _previousRequest;
            }
        }
        catch (Exception ex) { Mod.Warn("Restoring the source book texture's streaming request: " + ex.Message); }
        finally { _requestedSource = null; _reportedWait = false; _nextPoll = 0; }
    }

    // Isolate the holder in its original slot, leaving the other slot blank.
    // The source image and global graphics-quality settings are not edited.
    private static Texture2D Create(Texture2D source, bool sourceIsTop)
    {
        var w = source.width; var h = source.height;
        if (w < 8 || h < 8) throw new InvalidOperationException("The source book texture is too small.");
        RenderTexture? temporary = null; Texture2D? readback = null;
        Texture2D? result = null;
        var previous = RenderTexture.active;
        try
        {
            temporary = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readback.Apply();
            var input = readback.GetPixels32();
            var output = new Color32[w * h];
            var half = h / 2;
            for (var y = 0; y < h; y++)
            {
                // Empty paper is sampled from the original page's top margin.
                var sourceY = (y >= half) == sourceIsTop ? y : h - 2 - y % 4;
                for (var x = 0; x < w; x++) output[y * w + x] = input[sourceY * w + x];
            }
            result = new Texture2D(w, h, TextureFormat.RGBA32, true) {
                name = "LongStickSpear_StorageBookPage", hideFlags = HideFlags.DontUnloadUnusedAsset,
                ignoreMipmapLimit = true, filterMode = FilterMode.Trilinear, anisoLevel = 8,
                mipMapBias = 0f, wrapMode = TextureWrapMode.Clamp
            };
            result.SetPixels32(output); result.Apply(true, true);
            return result;
        }
        catch
        {
            if (result != null) Object.Destroy(result);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            if (readback != null) Object.Destroy(readback);
        }
    }
}
