await figma.setCurrentPageAsync(await figma.getNodeByIdAsync('0:1'));
await Promise.all([{family:'Rajdhani',style:'SemiBold'},{family:'IBM Plex Mono',style:'Regular'}].map(f=>figma.loadFontAsync(f)));
const section=await figma.getNodeByIdAsync('9:11');
if(section.children.some(n=>n.name==='07 / MAP EXCHANGE'))throw new Error('Request variants exist; inspect before retry.');
const created=[],mutated=[],frames={...design.frames};
const original=await figma.getNodeByIdAsync(design.frames.request);
for(const [i,key,label]of [[0,'request_map','MAP'],[1,'request_reserves','RESERVES']]){
 const f=original.clone();section.appendChild(f);f.name=(i===0?'07':'08')+' / '+label+' EXCHANGE';f.x=40+i*2020;f.y=3980;frames[key]=f.id;
 created.push(f.id,...f.findAll(()=>true).map(n=>n.id));
 const title=f.findOne(n=>n.type==='TEXT'&&n.name==='Exchange title');title.characters=label+' EXCHANGE';
}
const field=await figma.getNodeByIdAsync(design.frames.field),sent=field.clone();section.appendChild(sent);sent.name='09 / SUPPORT SENT';sent.x=4080;sent.y=3980;frames.sent=sent.id;created.push(sent.id,...sent.findAll(()=>true).map(n=>n.id));
const toastMaster=await figma.getNodeByIdAsync(design.masters.Toast),toast=toastMaster.createInstance();sent.appendChild(toast);toast.name='Toast';toast.x=744;toast.y=906;created.push(toast.id,...toast.findAll(()=>true).map(n=>n.id));
const transition={type:'SMART_ANIMATE',duration:.16,easing:{type:'EASE_OUT'}};
const nav=id=>({trigger:{type:'ON_CLICK'},actions:[{type:'NODE',destinationId:id,navigation:'NAVIGATE',transition,preserveScrollPosition:false}]});
const routes={channels:frames.channels,support:frames.support,close:frames.field,cancel:frames.field,exchange0:frames.request,exchange1:frames.request_map,exchange2:frames.request_reserves,send0:frames.sent,send1:frames.sent,send2:frames.sent};
let links=0;
for(const [state,id]of Object.entries(frames)){
 const f=await figma.getNodeByIdAsync(id);
 for(const g of layout.groups.filter(g=>g.action)){
  const n=f.children.find(n=>n.name===g.name);if(!n)continue;
  const existing=n.reactions.filter(r=>r.trigger?.type!=='ON_CLICK');
  const destination=g.action===state?frames.field:routes[g.action];
  await n.setReactionsAsync([...existing,nav(destination)]);mutated.push(n.id);links++;
 }
}
for(const g of layout.groups.filter(g=>g.action)){
 const base=await figma.getNodeByIdAsync(design.masters[g.name]);
 const hover=base.parent.children.find(n=>n.name==='State=Hover');await hover.setReactionsAsync([nav(routes[g.action])]);mutated.push(hover.id);
}
await sent.setReactionsAsync([{trigger:{type:'AFTER_TIMEOUT',timeout:2.5},actions:[{type:'NODE',destinationId:frames.field,navigation:'NAVIGATE',transition,preserveScrollPosition:false}]}]);mutated.push(sent.id);
section.resizeWithoutConstraints(6100,5140);mutated.push(section.id);
return {createdNodeIds:created,mutatedNodeIds:mutated,frames,clickLinks:links};
