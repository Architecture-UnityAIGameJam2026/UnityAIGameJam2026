// Inject layout and existing revision-2 design IDs. mode='weapons' upgrades the
// first silhouette version; mode='reference' updates only the floating weapon HUD.
await figma.setCurrentPageAsync(await figma.getNodeByIdAsync('0:1'));
await figma.loadFontAsync({family:'Rajdhani',style:'SemiBold'});
const created=[],mutated=[],removed=[];
const library=await figma.getNodeByIdAsync(design.library);
const field=await figma.getNodeByIdAsync(design.frames.field);
const weaponsOnly=typeof mode!=='undefined'&&['weapons','reference'].includes(mode);
if(!weaponsOnly&&field.children.find(n=>n.name==='Spatial')?.width===176)throw new Error('Icon revision already applied; inspect before retry.');
if(weaponsOnly&&mode==='weapons'&&field.children.find(n=>n.name==='Reserves')?.width===240)throw new Error('Weapon silhouettes already applied; inspect before retry.');
const targetGroups=weaponsOnly?layout.groups.filter(g=>g.name==='Reserves'):layout.groups;
const rgb=h=>({r:parseInt(h.slice(1,3),16)/255,g:parseInt(h.slice(3,5),16)/255,b:parseInt(h.slice(5,7),16)/255});
const localVars=await figma.variables.getLocalVariablesAsync('COLOR'),vars={};
for(const [name,color]of Object.entries(layout.colors))vars[color]=localVars.find(v=>v.name===name);
const paint=(color,opacity=1)=>{const p={type:'SOLID',color:rgb(color),opacity};return vars[color]?figma.variables.setBoundVariableForPaint(p,'color',vars[color]):p;};
const glow=color=>[{type:'DROP_SHADOW',color:{...rgb(color),a:.5},offset:{x:0,y:0},radius:7,spread:0,visible:true,blendMode:'SCREEN'}];
const track=n=>{created.push(n.id);return n;};
const path=points=>'M '+points.slice(0,2).join(' ')+' '+points.slice(2).reduce((s,v,i)=>s+(i%2===0?' L ':' ')+v,'');
function node(parent,n,g){
 let item;
 if(n.type==='text'){
  item=track(figma.createText());item.fontName={family:'Rajdhani',style:'SemiBold'};item.fontSize=n.size;item.characters=n.text;item.fills=[paint(n.color)];item.textAutoResize='WIDTH_AND_HEIGHT';parent.appendChild(item);item.x=n.x;item.y=n.y;
 }else if(n.type==='rect'){
  item=track(figma.createRectangle());item.resize(n.w,n.h);item.fills=[paint(n.color)];item.opacity=n.opacity??1;parent.appendChild(item);item.x=n.x;item.y=n.y;
 }else{
  let svg;
  if(n.type==='icon'){
   const definition=layout.icons.find(i=>i.name===n.icon);
   svg='<svg xmlns="http://www.w3.org/2000/svg" width="'+n.w+'" height="'+n.h+'" viewBox="0 0 '+definition.width+' '+definition.height+'">'+definition.strokes.map(s=>'<path d="'+path(s.points)+(s.fill?' Z':'')+'" fill="'+(s.fill?n.color:'none')+'" stroke="'+(s.fill?'none':n.color)+'" stroke-width="'+(n.stroke*definition.width/n.w)+'"/>').join('')+'</svg>';
  }else{
   let points=n.points;
   if(n.type==='arc'){points=[];const steps=Math.ceil(Math.abs(n.end-n.start)/3);for(let i=0;i<=steps;i++){const a=(n.start+(n.end-n.start)*i/steps)*Math.PI/180;points.push(n.x+n.w/2+Math.cos(a)*n.w/2,n.y+n.h/2+Math.sin(a)*n.h/2);}}
   svg='<svg xmlns="http://www.w3.org/2000/svg" width="'+g.w+'" height="'+g.h+'" viewBox="0 0 '+g.w+' '+g.h+'"><path d="'+path(points)+(n.type==='polygon'?' Z':'')+'" fill="'+(n.type==='polygon'?n.color:'none')+'" stroke="'+(n.type==='polygon'?'none':n.color)+'" stroke-width="'+(n.stroke??1)+'" opacity="'+(n.opacity??1)+'"/></svg>';
  }
  item=track(figma.createNodeFromSvg(svg));created.push(...item.findAll(()=>true).map(c=>c.id));parent.appendChild(item);item.x=n.type==='icon'?n.x:0;item.y=n.type==='icon'?n.y:0;
 }
 item.name=n.name;if(n.type==='icon')item.opacity=n.opacity??1;if(n.glow)item.effects=glow(n.color);return item;
}
function fill(c,g,hover=false){
 for(const child of [...c.children]){removed.push(child.id);child.remove();}
 c.resize(g.w,g.h);c.fills=[];c.clipsContent=false;
 for(const n of g.nodes)node(c,n,g);
 if(hover){const h=track(figma.createRectangle());c.insertChild(0,h);h.name='Hover bloom';h.resize(g.w,g.h);h.fills=[paint(layout.colors.cyan,.12)];h.effects=glow(layout.colors.cyan);}
 c.description='Compact icon HUD / '+g.name+'. '+(g.action?'Action: '+g.action+'. Minimum 44px hit area.':'State: '+g.state+'.')+' Shared vector geometry with Unity.';mutated.push(c.id);
}
const masters={};let staticIndex=0,interactiveIndex=0;
for(const g of targetGroups){
 const c=await figma.getNodeByIdAsync(design.masters[g.name]);masters[g.name]=c;fill(c,g);
 if(c.parent.type==='COMPONENT_SET'){
  const set=c.parent,h=set.children.find(n=>n.name==='State=Hover');fill(h,g,true);
  set.x=40+(interactiveIndex%4)*400;set.y=880+Math.floor(interactiveIndex/4)*190;set.resize(Math.max(240,g.w*2+48),g.h+40);c.x=16;c.y=20;h.x=g.w+32;h.y=20;interactiveIndex++;mutated.push(set.id);
 }else{c.name='HUD v3 / '+g.name;if(!weaponsOnly){c.x=40+(staticIndex%4)*400;c.y=40+Math.floor(staticIndex/4)*210;staticIndex++;}}
}
for(const [name,health,local]of (weaponsOnly?[]:[['VitalsCritical',12,false],['VitalsAwakened',92,true]])){
 const g=JSON.parse(JSON.stringify(layout.groups.find(g=>g.name==='Vitals')));
 for(const n of g.nodes){
  if(n.name==='Health number')n.text=String(health);
  if(n.name==='Health arc'){n.end=140+260*health/100;if(health<=15)n.color=layout.colors.red;}
  if(n.name==='Ownership icon'&&local){n.icon='self';n.color=layout.colors.amber;}
 }
 const c=await figma.getNodeByIdAsync(design.masters[name]);fill(c,g);c.name='HUD v3 / '+name;masters[name]=c;c.x=40+(staticIndex%4)*400;c.y=40+Math.floor(staticIndex/4)*210;staticIndex++;
}
for(const [name,kind]of (weaponsOnly?[]:[['ExchangeMap','map'],['ExchangeReserves','ammo']])){
 const g=JSON.parse(JSON.stringify(layout.groups.find(g=>g.name==='Exchange')));g.nodes.find(n=>n.name==='Channel icon').icon=kind;
 const c=track(figma.createComponent());library.appendChild(c);fill(c,g);c.name='HUD v3 / '+name;masters[name]=c;c.x=40+(staticIndex%4)*400;c.y=40+Math.floor(staticIndex/4)*210;staticIndex++;
}
const frames={...design.frames,request_map:'18:615',request_reserves:'18:814',sent:'18:1013'};
for(const [state,id]of Object.entries(frames)){
 const f=await figma.getNodeByIdAsync(id);mutated.push(f.id);
 for(const g of targetGroups){
  const instance=f.children.find(n=>n.name===g.name);if(!instance)continue;
  let key=g.name;
  if(key==='Vitals'&&state==='critical')key='VitalsCritical';if(key==='Vitals'&&state==='awakened')key='VitalsAwakened';
  if(key==='Exchange'&&state==='request_map')key='ExchangeMap';if(key==='Exchange'&&state==='request_reserves')key='ExchangeReserves';
  if((await instance.getMainComponentAsync())?.id!==masters[key].id)instance.swapComponent(masters[key]);
  instance.resize(g.w,g.h);instance.x=g.x;instance.y=g.y;mutated.push(instance.id);
 }
}
field.name='01 / ICON FIELD';library.resizeWithoutConstraints(1680,1500);mutated.push(library.id);
const counts={};for(const n of field.findAll(()=>true))counts[n.type]=(counts[n.type]||0)+1;
return {createdNodeIds:created,mutatedNodeIds:mutated,removedNodeIds:removed,frames,counts,defaultText:field.findAllWithCriteria({types:['TEXT']}).map(n=>n.characters),minimap:{x:48,y:40,width:176,height:188}};
