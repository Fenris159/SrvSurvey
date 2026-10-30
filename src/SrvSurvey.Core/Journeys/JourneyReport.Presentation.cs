namespace SrvSurvey.Core.Journeys;

public static partial class JourneyReport
{
    private const string Styles = """
        :root{color-scheme:dark;--space:#000012;--surface:#01011c;--instrument:#13293f;--accent:#3f87d4;--text:#e5e5e5;--muted:#c8c8c8;--line:#195494}
        *{box-sizing:border-box}body{margin:0;background:var(--space);color:var(--text);font:16px/1.6 "Segoe UI",Arial,sans-serif}
        main{max-width:1180px;margin:auto;padding:56px 40px}h1,h2,h3,h4,dd{font-family:Bahnschrift,"Trebuchet MS",sans-serif}h1{font-size:clamp(32px,5vw,60px);line-height:1.1;font-weight:600;margin:12px 0 20px;overflow-wrap:anywhere}
        h2{font-size:26px;margin:0 0 16px}h3{font-size:19px;margin:0 0 12px}h4{font-size:15px;color:var(--muted);margin:22px 0 8px}p{margin:0 0 12px}.brand{color:var(--accent)}.commander{font-size:20px}.description{max-width:76ch;white-space:pre-wrap}
        .period{display:flex;flex-wrap:wrap;gap:12px 30px;color:var(--muted);font-size:14px;align-items:center;margin-top:24px}.period button{margin-left:auto}button,input{font:inherit}button{border:1px solid var(--line);background:var(--instrument);color:var(--text);padding:9px 16px;border-radius:6px;cursor:pointer}button:hover{border-color:var(--accent)}:focus-visible{outline:3px solid var(--accent);outline-offset:3px}
        .route{margin:30px 0 46px;background:var(--surface);border-left:3px solid var(--accent);padding:22px 26px}.route-endpoints{display:flex;justify-content:space-between;gap:30px}.route-endpoints>div{display:flex;flex-direction:column;max-width:48%;overflow-wrap:anywhere}.route-endpoints>div:last-child{text-align:right}.route-endpoints span,figcaption{font-size:13px;color:var(--muted)}.route-endpoints strong{font:500 22px/1.4 Bahnschrift,"Trebuchet MS",sans-serif}
        svg{display:block;width:100%;height:auto;max-height:260px;margin:12px 0}.route-grid{stroke:var(--line);stroke-width:.6;opacity:.45;fill:none}.route-path{fill:none;stroke:var(--accent);stroke-width:2;stroke-linejoin:round;vector-effect:non-scaling-stroke}.departure{fill:var(--surface);stroke:var(--text);stroke-width:2}.arrival{fill:var(--accent);stroke:var(--text);stroke-width:2}
        main>section{margin:0 0 46px}.metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));margin:0 0 20px;gap:22px 32px}.metrics>div{border-top:1px solid var(--line);padding-top:12px}dt{color:var(--muted);font-size:14px}dd{margin:2px 0 0;font-size:26px;overflow-wrap:anywhere}.muted{color:var(--muted);font-size:14px}.stat-columns{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:40px;margin-top:28px}.facts{margin:0}.facts>div{display:flex;justify-content:space-between;gap:16px;padding:7px 0;border-bottom:1px solid #13293f}.facts dt{max-width:65%}.facts dd{font-size:15px;line-height:1.6;text-align:right;margin:0}.identifiers{overflow-wrap:anywhere;font-size:14px}
        .history-heading{display:flex;justify-content:space-between;gap:20px;align-items:center}.history-heading h2{margin-bottom:4px}output{color:var(--muted);white-space:nowrap}.history-tools{display:flex;flex-wrap:wrap;align-items:end;gap:16px 28px;margin:20px 0}.history-tools label{display:flex;flex-direction:column;gap:6px;font-size:14px}.history-tools input[type=search]{background:var(--surface);color:var(--text);border:1px solid var(--line);padding:11px 14px;border-radius:6px;width:360px;max-width:100%}.history-tools .expand-control{flex-direction:row;align-items:center;gap:8px;padding:10px 0}
        .history{list-style:none;margin:0;padding:0 20px 0 0;max-height:78vh;overflow:auto;scrollbar-color:var(--accent) var(--surface)}.visit{padding:22px 0 22px 28px;border-left:1px solid var(--line);border-bottom:1px solid var(--instrument);scroll-margin:16px}.visit-heading{display:flex;gap:16px;align-items:center}.visit-heading h3{margin:0;font-size:22px;overflow-wrap:anywhere}.visit-heading p{font-size:13px;color:var(--muted);margin:3px 0}.visit-number{font:500 20px Bahnschrift,"Trebuchet MS",sans-serif;color:var(--accent);min-width:24px}.leg-distance{margin-left:auto;color:var(--muted);font-size:14px;white-space:nowrap}.visit-readout{display:flex;gap:12px 24px;flex-wrap:wrap;margin:14px 0 12px 40px;font-size:14px;color:var(--muted)}.visit-readout strong{color:var(--text);font-weight:500}
        summary{cursor:pointer;color:var(--accent);padding:5px 0;display:list-item;margin-left:40px}details[open] summary{margin-bottom:16px}.visit-details{margin-left:40px}.visit-details .stat-columns{gap:30px}.system-notes{margin-top:20px;border-left:2px solid var(--accent);padding-left:18px}.system-notes p{white-space:pre-wrap;overflow-wrap:anywhere;max-width:76ch}.system-notes h4{margin-top:0}footer{font-size:13px;color:var(--muted);border-top:1px solid var(--line);padding-top:24px}[hidden]{display:none!important}
        @media(max-width:700px){main{padding:28px 20px}.period button{margin-left:0}.route{padding:18px 16px}.route-endpoints strong{font-size:17px}.metrics{grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}.metrics dd{font-size:22px}.stat-columns{grid-template-columns:1fr;gap:28px}.history-tools>label{width:100%}.history-tools input[type=search]{width:100%}.history{padding-right:10px}.visit{padding-left:14px}.visit-heading{flex-wrap:wrap;gap:8px 12px}.leg-distance{margin-left:36px}.visit-details,.visit-readout,summary{margin-left:0}.history-heading{align-items:start}output{font-size:13px}}
        @media print{color-scheme:light;body{background:white;color:#152331;font-size:10pt}main{max-width:none;padding:0}.brand,summary{color:#164c7d}.muted,dt,.period,figcaption,output,.visit-heading p,.leg-distance,.visit-readout{color:#425568}.route{background:#f1f6fa}.route-endpoints strong,.visit-readout strong{color:#152331}.history{max-height:none;overflow:visible;padding:0}.history-tools,button,#no-matches{display:none!important}.visit[hidden]{display:list-item!important}.visit{break-inside:avoid}.metrics dd{font-size:20pt}.facts>div{border-color:#c9d7e3}footer{color:#425568}.stat-columns{gap:24px}}
        """;

    private const string Script = """
        const rows=[...document.querySelectorAll('.visit')];
        const search=document.getElementById('system-search');
        search.addEventListener('input',()=>{
          const query=search.value.trim().toLocaleLowerCase();let shown=0;
          rows.forEach(row=>{row.hidden=!row.textContent.toLocaleLowerCase().includes(query);if(!row.hidden)shown++;});
          document.getElementById('visit-count').textContent=shown+' of '+rows.length+' visits';
          document.getElementById('no-matches').hidden=shown!==0;
        });
        document.getElementById('expand-visits').addEventListener('change',event=>{
          document.querySelectorAll('.visit details').forEach(detail=>detail.open=event.target.checked);
        });
        document.getElementById('print-report').addEventListener('click',()=>window.print());
        let printState=[];
        window.addEventListener('beforeprint',()=>{printState=[...document.querySelectorAll('details')].map(detail=>[detail,detail.open]);printState.forEach(([detail])=>detail.open=true);});
        window.addEventListener('afterprint',()=>printState.forEach(([detail,open])=>detail.open=open));
        """;
}
