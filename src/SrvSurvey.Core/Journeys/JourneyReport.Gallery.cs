using System.Text;

namespace SrvSurvey.Core.Journeys;

public sealed record JourneyReportImage
{
    public JourneyReportImage(string caption, string imagePath, string previewPath)
    {
        Caption = caption;
        ImagePath = ValidatePath(imagePath);
        PreviewPath = ValidatePath(previewPath);
    }

    public string Caption { get; }
    public string ImagePath { get; }
    public string PreviewPath { get; }

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (
            !path.StartsWith("images/", StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal)
            || !path.All(character => char.IsAsciiLetterOrDigit(character) || character is '/' or '-' or '_' or '.')
            || !path.EndsWith(".png", StringComparison.Ordinal)
        )
        {
            throw new ArgumentException("Report images must use relative PNG paths inside images/.", nameof(path));
        }
        return path;
    }
}

public static partial class JourneyReport
{
    private static void AppendGallery(StringBuilder html, string systemName, IReadOnlyList<JourneyReportImage>? images)
    {
        if (images is null || images.Count == 0)
        {
            return;
        }
        html.Append("<section class=\"system-gallery\" aria-label=\"Screenshots from ")
            .Append(E(systemName))
            .Append(
                "\"><h4>Screenshots</h4><p class=\"muted\">Available images from all visits to this system. Select a preview to enlarge it.</p><div class=\"screenshot-grid\">"
            );
        foreach (JourneyReportImage image in images)
        {
            string caption = systemName + " — " + image.Caption;
            html.Append("<a class=\"screenshot-tile\" href=\"")
                .Append(E(image.ImagePath))
                .Append("\" data-caption=\"")
                .Append(E(caption))
                .Append("\"><img loading=\"lazy\" decoding=\"async\" src=\"")
                .Append(E(image.PreviewPath))
                .Append("\" alt=\"")
                .Append(E(caption))
                .Append("\" width=\"640\" height=\"400\"><span>")
                .Append(E(image.Caption))
                .Append("</span></a>");
        }
        html.Append("</div></section>");
    }

    private const string ImageViewer = """
        <dialog id="image-viewer" aria-labelledby="image-caption">
          <div class="image-toolbar"><output id="image-position" aria-live="polite"></output><a id="image-original" target="_blank" rel="noopener">Open original</a><button id="image-close" type="button" autofocus>Close</button></div>
          <img id="image-full" alt=""><p id="image-caption"></p>
          <div class="image-navigation"><button id="image-previous" type="button">Previous</button><span>Arrow keys to browse · Escape to close</span><button id="image-next" type="button">Next</button></div>
        </dialog>
        """;

    private const string GalleryStyles = """
        .system-gallery{margin:18px 0 22px 40px}.system-gallery h4{color:var(--text);margin:0 0 6px}.screenshot-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:18px 14px}.screenshot-tile{color:var(--muted);text-decoration:none;min-width:0;font-size:12px}.screenshot-tile img{display:block;width:100%;height:auto;aspect-ratio:16/10;object-fit:cover;background:var(--surface);border-radius:4px;outline:1px solid var(--instrument)}.screenshot-tile:hover img{outline-color:var(--accent)}.screenshot-tile span{display:block;margin-top:7px;overflow-wrap:anywhere}
        #image-viewer{color:var(--text);background:var(--space);border:1px solid var(--line);border-radius:8px;padding:18px;width:calc(100vw - 36px);max-width:1500px;max-height:96vh;overflow:auto}#image-viewer::backdrop{background:rgba(0,0,12,.92)}.image-toolbar,.image-navigation{display:flex;align-items:center;gap:16px}.image-toolbar{margin-bottom:14px}.image-toolbar a{margin-left:auto;color:var(--accent);font-size:14px}#image-full{display:block;width:100%;height:70vh;object-fit:contain}#image-caption{font-size:14px;margin:12px 0;overflow-wrap:anywhere}.image-navigation{justify-content:space-between}.image-navigation span{font-size:12px;color:var(--muted)}.image-navigation button:disabled{opacity:.4;cursor:default}
        @media(max-width:700px){.system-gallery{margin-left:0}.screenshot-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:14px 10px}#image-viewer{padding:12px}#image-full{height:60vh}.image-navigation span{display:none}}
        @media print{.system-gallery{break-inside:auto}.screenshot-grid{grid-template-columns:repeat(3,minmax(0,1fr))}.screenshot-tile{break-inside:avoid;color:#425568}#image-viewer{display:none!important}}
        """;

    private const string GalleryScript = """
        const viewer=document.getElementById('image-viewer');
        let imageLinks=[],imageIndex=0,imageTrigger=null;
        function showImage(){
          const link=imageLinks[imageIndex],caption=link.dataset.caption;
          const image=document.getElementById('image-full');image.src=link.getAttribute('href');image.alt=caption;
          document.getElementById('image-original').href=link.getAttribute('href');
          document.getElementById('image-caption').textContent=caption;
          document.getElementById('image-position').textContent=(imageIndex+1)+' of '+imageLinks.length;
          document.getElementById('image-previous').disabled=imageIndex===0;
          document.getElementById('image-next').disabled=imageIndex===imageLinks.length-1;
          const active=document.activeElement;
          if(viewer.open&&(!viewer.contains(active)||active.disabled))document.getElementById('image-close').focus();
        }
        function moveImage(delta){const next=imageIndex+delta;if(next>=0&&next<imageLinks.length){imageIndex=next;showImage();}}
        document.querySelectorAll('.screenshot-tile').forEach(link=>link.addEventListener('click',event=>{
          if(typeof viewer.showModal!=='function'||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;
          event.preventDefault();imageTrigger=link;imageLinks=[...link.closest('.screenshot-grid').querySelectorAll('.screenshot-tile')];imageIndex=imageLinks.indexOf(link);showImage();viewer.showModal();
        }));
        document.getElementById('image-close').addEventListener('click',()=>viewer.close());
        document.getElementById('image-previous').addEventListener('click',()=>moveImage(-1));
        document.getElementById('image-next').addEventListener('click',()=>moveImage(1));
        viewer.addEventListener('keydown',event=>{
          if(event.key==='ArrowRight'){event.preventDefault();moveImage(1);}
          else if(event.key==='ArrowLeft'){event.preventDefault();moveImage(-1);}
          else if(event.key==='Tab'){
            const controls=[...viewer.querySelectorAll('a[href],button:not(:disabled)')];
            const first=controls[0],last=controls[controls.length-1];
            if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus();}
            else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}
          }
        });
        viewer.addEventListener('click',event=>{if(event.target===viewer)viewer.close();});
        viewer.addEventListener('close',()=>{document.getElementById('image-full').removeAttribute('src');if(imageTrigger)imageTrigger.focus();});
        """;
}
