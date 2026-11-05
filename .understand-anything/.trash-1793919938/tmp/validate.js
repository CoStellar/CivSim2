const t=require("../intermediate/tour.json"),r=require("./ua-tour-results.json");
const bad=[];t.forEach((s,i)=>{if(s.order!==i+1)bad.push("order"+i);s.nodeIds.forEach(id=>{if(!r.nodeSummaryIndex[id])bad.push(id)})});
console.log(t.length,bad);
