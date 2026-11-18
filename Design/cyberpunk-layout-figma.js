// Revision 6: recompose the full HUD into the four corners of the reference.
// Inject shared layout and existing component/frame IDs as design.
await figma.setCurrentPageAsync(await figma.getNodeByIdAsync('0:1'));
await figma.loadFontAsync({family:'Rajdhani',style:'SemiBold'});
const created=[],mutated=[],removed=[];
const library=await figma.getNodeByIdAsync(design.library);
const frames={...design.frames,request_map:'18:615',request_reserves:'18:814',sent:'18:1013'};
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
const masters={},hoverMasters={};let staticIndex=0,interactiveIndex=0;
for(const g of layout.groups){
 let c=design.masters[g.name]?await figma.getNodeByIdAsync(design.masters[g.name]):null;
 if(!c){c=track(figma.createComponent());library.appendChild(c);}
 fill(c,g);masters[g.name]=c;
 if(g.action){
  let set,h;
  if(c.parent.type==='COMPONENT_SET'){set=c.parent;h=set.children.find(n=>n.name==='State=Hover');}
  else{
   h=track(c.clone());library.appendChild(h);created.push(...h.findAll(()=>true).map(n=>n.id));
   c.name='State=Default';h.name='State=Hover';set=track(figma.combineAsVariants([c,h],library));set.name='Interactive / '+g.name;
  }
  fill(h,g,true);hoverMasters[g.name]=h;
  set.x=40+(interactiveIndex%4)*450;set.y=1000+Math.floor(interactiveIndex/4)*200;
  set.resize(Math.max(240,g.w*2+48),g.h+40);c.x=16;c.y=20;h.x=g.w+32;h.y=20;interactiveIndex++;mutated.push(set.id);
 }else{c.name='HUD v6 / '+g.name;c.x=40+(staticIndex%4)*450;c.y=40+Math.floor(staticIndex/4)*230;staticIndex++;}
}
for(const [name,health,local]of [['VitalsCritical',12,false],['VitalsAwakened',92,true]]){
 const g=JSON.parse(JSON.stringify(layout.groups.find(g=>g.name==='Vitals')));
 for(const n of g.nodes){
  if(n.name==='Health number')n.text=String(health);
  if(n.name==='Health bar'){n.w=280*health/100;n.color=local?layout.colors.cyan:health<=15?'#FF404D':layout.colors.red;}
  if(n.name==='Ownership icon'&&local){n.icon='self';n.color=layout.colors.amber;}
 }
 const c=await figma.getNodeByIdAsync(design.masters[name]);fill(c,g);c.name='HUD v6 / '+name;masters[name]=c;
 c.x=40+(staticIndex%4)*450;c.y=40+Math.floor(staticIndex/4)*230;staticIndex++;
}
for(const [name,kind]of [['ExchangeMap','map'],['ExchangeReserves','ammo']]){
 const g=JSON.parse(JSON.stringify(layout.groups.find(g=>g.name==='Exchange')));g.nodes.find(n=>n.name==='Channel icon').icon=kind;
 let c=library.children.find(n=>n.type==='COMPONENT'&&n.name.endsWith('/ '+name));
 if(!c){c=track(figma.createComponent());library.appendChild(c);}
 fill(c,g);c.name='HUD v6 / '+name;masters[name]=c;c.x=40+(staticIndex%4)*450;c.y=40+Math.floor(staticIndex/4)*230;staticIndex++;
}
const transition={type:'SMART_ANIMATE',duration:.16,easing:{type:'EASE_OUT'}};
const reaction=(type,id,navigation='NAVIGATE')=>({trigger:{type},actions:[{type:'NODE',destinationId:id,navigation,transition,preserveScrollPosition:false}]});
const routes={channels:frames.channels,support:frames.support,close:frames.field,cancel:frames.field,exchange0:frames.request,exchange1:frames.request_map,exchange2:frames.request_reserves,send0:frames.sent,send1:frames.sent,send2:frames.sent};
for(const g of layout.groups.filter(g=>g.action)){
 await masters[g.name].setReactionsAsync([reaction('ON_HOVER',hoverMasters[g.name].id,'CHANGE_TO')]);
 await hoverMasters[g.name].setReactionsAsync([reaction('ON_CLICK',routes[g.action])]);
}
let clickLinks=0;
for(const [state,id]of Object.entries(frames)){
 const f=await figma.getNodeByIdAsync(id);mutated.push(f.id);
 const logical=state.startsWith('request')?'request':state==='sent'?'toast':state;
 for(const g of layout.groups){
  let instance=f.children.find(n=>n.type==='INSTANCE'&&n.name===g.name);
  const visible=g.state==='all'||g.state===logical||(g.state==='tray'&&['channels','support'].includes(state));
  if(!visible){if(instance){removed.push(instance.id);instance.remove();}continue;}
  let key=g.name;
  if(key==='Vitals'&&state==='critical')key='VitalsCritical';if(key==='Vitals'&&state==='awakened')key='VitalsAwakened';
  if(key==='Exchange'&&state==='request_map')key='ExchangeMap';if(key==='Exchange'&&state==='request_reserves')key='ExchangeReserves';
  if(!instance){instance=track(masters[key].createInstance());instance.name=g.name;created.push(...instance.findAll(()=>true).map(n=>n.id));}
  else if((await instance.getMainComponentAsync())?.id!==masters[key].id)instance.swapComponent(masters[key]);
  f.appendChild(instance);instance.resize(g.w,g.h);instance.x=g.x;instance.y=g.y;mutated.push(instance.id);
  if(g.action){
   let destination=g.action===state?frames.field:routes[g.action];
   if(destination===f.id)destination=frames.field;
   await instance.setReactionsAsync([reaction('ON_HOVER',hoverMasters[g.name].id,'CHANGE_TO'),reaction('ON_CLICK',destination)]);clickLinks++;
  }
 }
}
const field=await figma.getNodeByIdAsync(frames.field);field.name='01 / CYBERPUNK CORNER LAYOUT';
library.resizeWithoutConstraints(1880,1880);mutated.push(library.id);
const positions=Object.fromEntries(field.children.filter(n=>n.type==='INSTANCE').map(n=>[n.name,{x:n.x,y:n.y,w:n.width,h:n.height}]));
if(field.children.some(n=>n.type==='INSTANCE'&&n.x>500&&n.x<1350&&n.y>750))throw new Error('Bottom center is not clear.');
return {createdNodeIds:created,mutatedNodeIds:mutated,removedNodeIds:removed,frames,masters:Object.fromEntries(Object.entries(masters).map(([k,v])=>[k,v.id])),library:library.id,clickLinks,interactiveSets:interactiveIndex,positions,defaultText:field.findAllWithCriteria({types:['TEXT']}).map(n=>n.characters)};
