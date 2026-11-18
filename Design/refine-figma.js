// Run through official use_figma with the shared layout object injected by caller.
const page=await figma.getNodeByIdAsync('0:1');await figma.setCurrentPageAsync(page);
const section=await figma.getNodeByIdAsync('9:11');
if(page.children.some(n=>n.name==='SENSELINK / INTERACTIVE COMPONENTS'))throw new Error('Revision 2 exists; inspect before retry.');
await Promise.all([{family:'Rajdhani',style:'SemiBold'},{family:'IBM Plex Mono',style:'Regular'}].map(f=>figma.loadFontAsync(f)));
const created=[],mutated=[],removed=[];
const track=n=>{created.push(n.id);return n;};
const rgb=h=>({r:parseInt(h.slice(1,3),16)/255,g:parseInt(h.slice(3,5),16)/255,b:parseInt(h.slice(5,7),16)/255});
const localVars=await figma.variables.getLocalVariablesAsync('COLOR');
const vars={};for(const [name,color]of Object.entries(layout.colors))vars[color]=localVars.find(v=>v.name===name);
const paint=(color,opacity=1)=>{const p={type:'SOLID',color:rgb(color),opacity};return vars[color]?figma.variables.setBoundVariableForPaint(p,'color',vars[color]):p;};
const glow=color=>[{type:'DROP_SHADOW',color:{...rgb(color),a:.55},offset:{x:0,y:0},radius:9,spread:0,visible:true,blendMode:'SCREEN'}];
function label(parent,n){
 const t=track(figma.createText());t.name=n.name;t.fontName={family:n.font==='display'?'Rajdhani':'IBM Plex Mono',style:n.font==='display'?'SemiBold':'Regular'};
 t.fontSize=n.size;t.characters=n.text;t.fills=[paint(n.color)];t.textAutoResize='WIDTH_AND_HEIGHT';parent.appendChild(t);t.x=n.x;t.y=n.y;return t;
}
function node(parent,n,g){
 if(n.type==='text')return label(parent,n);
 let item;
 if(n.type==='rect'){
  item=track(figma.createRectangle());item.resize(n.w,n.h);item.fills=[paint(n.color)];item.opacity=n.opacity??1;parent.appendChild(item);item.x=n.x;item.y=n.y;
 }else{
  let points=n.points;
  if(n.type==='arc'){points=[];const steps=Math.ceil(Math.abs(n.end-n.start)/3);for(let i=0;i<=steps;i++){const a=(n.start+(n.end-n.start)*i/steps)*Math.PI/180;points.push(n.x+n.w/2+Math.cos(a)*n.w/2,n.y+n.h/2+Math.sin(a)*n.h/2);}}
  const d='M '+points.slice(0,2).join(' ')+' '+points.slice(2).reduce((s,v,i)=>s+(i%2===0?' L ':' ')+v,'')+(n.type==='polygon'?' Z':'');
  const svg='<svg xmlns="http://www.w3.org/2000/svg" width="'+g.w+'" height="'+g.h+'" viewBox="0 0 '+g.w+' '+g.h+'"><path d="'+d+'" fill="'+(n.type==='polygon'?n.color:'none')+'" stroke="'+(n.type==='polygon'?'none':n.color)+'" stroke-width="'+(n.stroke??1)+'" opacity="'+(n.opacity??1)+'"/></svg>';
  item=track(figma.createNodeFromSvg(svg));parent.appendChild(item);item.x=0;item.y=0;created.push(...item.findAll(()=>true).map(x=>x.id));
 }
 item.name=n.name;if(n.glow)item.effects=glow(n.color);return item;
}
const library=track(figma.createSection());library.name='SENSELINK / INTERACTIVE COMPONENTS';library.x=6600;library.y=200;library.resizeWithoutConstraints(2400,2700);
const masters={};let idx=0;
for(const g of layout.groups){
 const c=track(figma.createComponent());c.name='HUD v2 / '+g.name;c.resize(g.w,g.h);c.fills=[];c.clipsContent=false;library.appendChild(c);c.x=40+(idx%3)*780;c.y=40+Math.floor(idx/3)*320;idx++;
 c.description='Unity UGUI 1920×1080. '+(g.action?'Interactive action: '+g.action+'. Hover accent blooms; click activates.':'Visible when '+g.state+'.');
 for(const n of g.nodes)node(c,n,g);
 // Button labels form a horizontal row so key and label stay aligned when edited.
 if(g.nodes.some(n=>n.name==='Label')){
  const key=c.findOne(n=>n.type==='TEXT'&&n.name==='Key'),title=c.findOne(n=>n.type==='TEXT'&&n.name==='Label');
  const row=track(figma.createAutoLayout('HORIZONTAL'));row.name='Button content';c.appendChild(row);row.fills=[];row.itemSpacing=22;row.counterAxisAlignItems='CENTER';row.x=16;row.y=12;
  if(key.characters)row.appendChild(key);else{removed.push(key.id);key.remove();}row.appendChild(title);
 }
 masters[g.name]=c;
}
function variant(base,name,health,owner){
 const c=track(masters[base].clone());library.appendChild(c);created.push(...c.findAll(()=>true).map(n=>n.id));c.name='HUD v2 / '+name;c.x=40+(idx%3)*780;c.y=40+Math.floor(idx/3)*320;idx++;
 for(const t of c.findAllWithCriteria({types:['TEXT']})){
  if(t.name==='Health number')t.characters=String(health);
  if(t.name==='Health state')t.characters=health<=15?'CRITICAL':'RESTORED';
  if(t.name==='Owner'&&owner)t.characters=owner;
 }
 const old=c.findOne(n=>n.name==='Health arc');removed.push(old.id);old.remove();
 const g=layout.groups.find(g=>g.name===base),arc=g.nodes.find(n=>n.name==='Health arc');node(c,{...arc,end:140+260*health/100,color:health<=15?layout.colors.red:layout.colors.cyan},g);
 masters[name]=c;
}
variant('Vitals','VitalsCritical',12,null);variant('Vitals','VitalsAwakened',92,'YOU / VITALS');
const frames={};
for(const [state,id]of [['field','9:577'],['request','9:994'],['critical','9:1423'],['awakened','9:1852']])frames[state]=await figma.getNodeByIdAsync(id);
const background=frames.field.children.find(n=>n.name==='Relay chamber / editable spatial sketch');
for(const [i,state]of ['channels','support'].entries()){
 const f=track(figma.createFrame());section.appendChild(f);f.name=state==='channels'?'05 / LINK CHANNELS':'06 / SUPPORT TRAY';f.resize(1920,1080);f.fills=[];f.clipsContent=true;f.x=4080;f.y=130+i*1180;
 const bg=track(background.clone());f.appendChild(bg);bg.x=0;bg.y=0;created.push(...bg.findAll(()=>true).map(n=>n.id));frames[state]=f;
}
const instances={};
for(const [state,f]of Object.entries(frames)){
 for(const c of [...f.children])if(c.type==='INSTANCE'||c.name==='Danger edge vignette'){removed.push(c.id);c.remove();}
 mutated.push(f.id);f.name=f.name.replace('CROSSED SENSES','QUIET FIELD');instances[state]={};
 for(const g of layout.groups){
  if(g.state!=='all'&&g.state!==state&&!(g.state==='tray'&&(state==='channels'||state==='support')))continue;
  let key=g.name;if(key==='Vitals'&&state==='critical')key='VitalsCritical';if(key==='Vitals'&&state==='awakened')key='VitalsAwakened';
  const n=track(masters[key].createInstance());f.appendChild(n);n.name=g.name;n.x=g.x;n.y=g.y;instances[state][g.name]=n;created.push(...n.findAll(()=>true).map(n=>n.id));
 }
}
section.resizeWithoutConstraints(6100,section.height);
library.resizeWithoutConstraints(2400,40+Math.ceil(idx/3)*320);
mutated.push(section.id);
return {createdNodeIds:created,mutatedNodeIds:mutated,removedNodeIds:removed,frames:Object.fromEntries(Object.entries(frames).map(([s,n])=>[s,n.id])),masters:Object.fromEntries(Object.entries(masters).map(([s,n])=>[s,n.id])),instances:Object.fromEntries(Object.entries(instances).map(([s,ns])=>[s,Object.fromEntries(Object.entries(ns).map(([k,n])=>[k,n.id]))])),library:library.id};
