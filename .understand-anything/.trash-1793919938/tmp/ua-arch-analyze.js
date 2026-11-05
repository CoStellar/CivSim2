const fs=require('fs');
try{
const d=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));const N=d.fileNodes,E=d.allEdges||[],IE=d.importEdges||[];
const byId=Object.fromEntries(N.map(n=>[n.id,n]));
const grp=p=>{const s=p.split('/');if(s.length===1)return'(root)';if((s[0]==='src'||s[0]==='tests')&&s.length>2){return s.length>3?s[1]+'/'+s[2]:s[1];}return s.length>2&&s[0]==='docs'?'docs/'+s[1]:s[0];};
const G={},T={};N.forEach(n=>{const g=grp(n.filePath);(G[g]=G[g]||[]).push(n.id);(T[n.type]=T[n.type]||[]).push(n.id)});
const gOf=id=>byId[id]?grp(byId[id].filePath):null;
const cc={},ig={},dens={},fin={},fout={};
E.forEach(e=>{const a=byId[e.source],b=byId[e.target];if(!a||!b)return;const k=a.type+'|'+b.type+'|'+e.type;cc[k]=(cc[k]||0)+1;
const ga=gOf(e.source),gb=gOf(e.target);fout[e.source]=(fout[e.source]||0)+1;fin[e.target]=(fin[e.target]||0)+1;
[ga,gb].forEach(g=>{dens[g]=dens[g]||{internalEdges:0,totalEdges:0}});if(ga===gb){dens[ga].internalEdges++;dens[ga].totalEdges++}else{dens[ga].totalEdges++;dens[gb].totalEdges++;const k2=ga+'>'+gb;ig[k2]=(ig[k2]||0)+1}});
Object.values(dens).forEach(v=>v.density=+(v.internalEdges/Math.max(1,v.totalEdges)).toFixed(2));
const pat={api:['routes','api','controllers'],service:['services','core','lib','domain','logic','internal'],data:['models','db','data','persistence'],utility:['utils','helpers','common','shared','tools'],config:['config','settings'],test:['tests','test'],types:['types','schemas'],documentation:['docs'],'ci-cd':['.github'],entry:['bin','cmd']};
const pm={};Object.keys(G).forEach(g=>{const last=g.split('/').pop().toLowerCase();for(const[l,a]of Object.entries(pat))if(a.includes(last)||a.includes(g.split('/')[0]))pm[g]=l});
const dir=[];const seen=new Set();Object.keys(ig).forEach(k=>{const[a,b]=k.split('>');const key=[a,b].sort().join('|');if(seen.has(key))return;seen.add(key);const ab=ig[a+'>'+b]||0,ba=ig[b+'>'+a]||0;dir.push(ab>=ba?{dependent:a,dependsOn:b,ab,ba}:{dependent:b,dependsOn:a,ab:ba,ba:ab})});
const fp=N.map(n=>n.filePath);
const out={scriptCompleted:true,directoryGroups:G,nodeTypeGroups:T,
crossCategoryEdges:Object.entries(cc).map(([k,c])=>{const[f,t,e]=k.split('|');return{fromType:f,toType:t,edgeType:e,count:c}}),
interGroupImports:Object.entries(ig).map(([k,c])=>{const[f,t]=k.split('>');return{from:f,to:t,count:c}}).sort((a,b)=>b.count-a.count),
intraGroupDensity:dens,patternMatches:pm,
deploymentTopology:{hasDockerfile:fp.some(p=>/Dockerfile/.test(p)),hasCompose:fp.some(p=>/docker-compose/.test(p)),hasK8s:false,hasTerraform:fp.some(p=>/\.tf$/.test(p)),hasCI:fp.some(p=>p.startsWith('.github/')),infraFiles:fp.filter(p=>p.startsWith('.github/')||/Dockerfile|Makefile/.test(p))},
dataPipeline:{schemaFiles:fp.filter(p=>/\.(sql|graphql|proto)$/.test(p)),migrationFiles:fp.filter(p=>/migrations\//.test(p)),dataModelFiles:fp.filter(p=>/\/State\/|\/Content\//.test(p)),apiHandlerFiles:fp.filter(p=>/CivSim\.Protocol\/Messages|CivSim\.Server\//.test(p))},
docCoverage:(()=>{const gs=Object.keys(G);const w=gs.filter(g=>G[g].some(id=>byId[id].type==='document')||E.some(e=>e.type==='documents'&&gOf(e.target)===g));return{groupsWithDocs:w.length,totalGroups:gs.length,coverageRatio:+(w.length/gs.length).toFixed(2),undocumentedGroups:gs.filter(g=>!w.includes(g))}})(),
dependencyDirection:dir,fileStats:{totalFileNodes:N.length,filesPerGroup:Object.fromEntries(Object.entries(G).map(([g,a])=>[g,a.length])),nodeTypeCounts:Object.fromEntries(Object.entries(T).map(([t,a])=>[t,a.length]))},
fileFanIn:Object.fromEntries(Object.entries(fin).sort((a,b)=>b[1]-a[1]).slice(0,25)),fileFanOut:Object.fromEntries(Object.entries(fout).sort((a,b)=>b[1]-a[1]).slice(0,25))};
fs.writeFileSync(process.argv[3],JSON.stringify(out,null,1));}catch(e){console.error(e.stack);process.exit(1)}
