const fs=require('fs');
try{
const inp=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
const nodes=inp.nodes||[],edges=inp.edges||[],layers=inp.layers||[];
const byId=new Map(nodes.map(n=>[n.id,n]));
const fi=new Map(),fo=new Map();nodes.forEach(n=>{fi.set(n.id,new Set());fo.set(n.id,new Set());});
for(const e of edges){if(!byId.has(e.source)||!byId.has(e.target)||e.source===e.target)continue;fo.get(e.source).add(e.target);fi.get(e.target).add(e.source);}
const rank=(m,k)=>nodes.map(n=>({id:n.id,[k]:m.get(n.id).size,name:n.name})).sort((a,b)=>b[k]-a[k]||a.id.localeCompare(b.id)).slice(0,20);
const fanIn=rank(fi,'fanIn'),fanOut=rank(fo,'fanOut');
const fos=nodes.map(n=>fo.get(n.id).size).sort((a,b)=>a-b),fis=nodes.map(n=>fi.get(n.id).size).sort((a,b)=>a-b);
const p90=fos[Math.floor(fos.length*0.9)]||0,p25=fis[Math.floor(fis.length*0.25)]||0;
const names=new Set('index.ts index.js main.ts main.js app.ts app.js server.ts server.js mod.rs main.go main.py main.rs manage.py app.py wsgi.py asgi.py run.py __main__.py Application.java Main.java Program.cs config.ru index.php App.swift Application.kt main.cpp main.c'.split(' '));
const cands=nodes.map(n=>{let s=0;const fp=(n.filePath||'').split(String.fromCharCode(92)).join('/');const depth=fp.split('/').length-1;
if(n.type==='document'){if(/^readme\.md$/i.test(fp))s+=5;else if(depth===0&&/\.md$/i.test(fp))s+=2;}
else if(n.type==='file'){if(names.has(n.name))s+=3;if(depth<=1)s+=1;if(fo.get(n.id).size>=p90&&p90>0)s+=1;if(fi.get(n.id).size<=p25)s+=1;}
return {id:n.id,score:s,name:n.name,summary:n.summary,type:n.type};}).filter(c=>c.score>0).sort((a,b)=>b.score-a.score||a.id.localeCompare(b.id));
const entry=cands.slice(0,5);
const codeStart=(cands.find(c=>c.type==='file'&&c.name==='Program.cs'&&/Headless/.test(c.id))||cands.find(c=>c.type==='file'));
const adj=new Map();for(const e of edges){if(e.type!=='imports'&&e.type!=='calls')continue;if(!byId.has(e.target))continue;if(!adj.has(e.source))adj.set(e.source,[]);adj.get(e.source).push(e.target);}
const order=[],depthMap={},byDepth={};
if(codeStart){const q=[codeStart.id];depthMap[codeStart.id]=0;while(q.length){const c=q.shift();order.push(c);const d=depthMap[c];(byDepth[d]=byDepth[d]||[]).push(c);for(const t of [...new Set(adj.get(c)||[])].sort()){if(!(t in depthMap)){depthMap[t]=d+1;q.push(t);}}}}
const cat={documentation:[],infrastructure:[],data:[],config:[]};
for(const n of nodes){const o={id:n.id,name:n.name,type:n.type,summary:n.summary};
if(n.type==='document')cat.documentation.push(o);else if(['service','pipeline','resource'].includes(n.type))cat.infrastructure.push(o);else if(['table','schema','endpoint'].includes(n.type))cat.data.push(o);else if(n.type==='config')cat.config.push(o);}
const rel=new Map();for(const e of edges){if(e.type!=='imports'&&e.type!=='calls')continue;rel.set(e.source+'|'+e.target+'|'+e.type,1);}
const und=new Map();const addU=(a,b)=>{if(!und.has(a))und.set(a,new Set());und.get(a).add(b);};
for(const e of edges){if(byId.has(e.source)&&byId.has(e.target)&&e.source!==e.target){addU(e.source,e.target);addU(e.target,e.source);}}
const clusters=[];const seen=new Set();
for(const k of rel.keys()){const [a,b,t]=k.split('|');if(a<b&&rel.has(b+'|'+a+'|'+t)){const key=a+'|'+b;if(seen.has(key))continue;seen.add(key);
const cl=new Set([a,b]);for(const n of nodes){if(cl.size>=5)break;if(cl.has(n.id))continue;let c=0;for(const m of cl)if(und.get(n.id)?.has(m))c++;if(c>=2)cl.add(n.id);}
const arr=[...cl];let ec=0;for(const e of edges)if(cl.has(e.source)&&cl.has(e.target))ec++;clusters.push({nodes:arr,edgeCount:ec});}}
clusters.sort((x,y)=>y.edgeCount-x.edgeCount);
const idx={};for(const n of nodes)idx[n.id]={name:n.name,type:n.type,summary:n.summary};
fs.writeFileSync(process.argv[3],JSON.stringify({scriptCompleted:true,entryPointCandidates:entry,fanInRanking:fanIn,fanOutRanking:fanOut,bfsTraversal:{startNode:codeStart?codeStart.id:null,order,depthMap,byDepth},nonCodeFiles:cat,clusters:clusters.slice(0,10),layers:{count:layers.length,list:layers},nodeSummaryIndex:idx,totalNodes:nodes.length,totalEdges:edges.length},null,1));
}catch(e){console.error(e.stack);process.exit(1);}
