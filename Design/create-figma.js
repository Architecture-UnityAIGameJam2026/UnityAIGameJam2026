// Executed through Figma's official use_figma tool; layout is injected by the caller.
const page=await figma.getNodeByIdAsync('0:1');
await figma.setCurrentPageAsync(page);
if(page.children.some(n=>n.name==='ON YOUR MARK / SENSELINK'))throw new Error('Design already exists; inspect it before retrying.');
await Promise.all([{family:'Rajdhani',style:'SemiBold'},{family:'IBM Plex Mono',style:'Regular'}].map(f=>figma.loadFontAsync(f)));
const created=[];
const track=n=>{created.push(n.id);return n;};
const rgb=h=>({r:parseInt(h.slice(1,3),16)/255,g:parseInt(h.slice(3,5),16)/255,b:parseInt(h.slice(5,7),16)/255});
const solid=(h,opacity=1)=>({type:'SOLID',color:rgb(h),opacity});
const collection=figma.variables.createVariableCollection('SenseLink / HUD palette');
const variables={};
for(const [name,hex] of Object.entries(layout.colors)){let v=figma.variables.createVariable(name,collection,'COLOR');v.scopes=['FRAME_FILL','SHAPE_FILL','TEXT_FILL','STROKE_COLOR'];v.setValueForMode(collection.defaultModeId,{...rgb(hex),a:1});variables[hex]=v;}
const paint=(h,o=1)=>variables[h]?figma.variables.setBoundVariableForPaint(solid(h,o),'color',variables[h]):solid(h,o);
function label(parent,name,str,x,y,size,color,font='mono'){
 const n=track(figma.createText());n.name=name;n.fontName={family:font==='display'?'Rajdhani':'IBM Plex Mono',style:font==='display'?'SemiBold':'Regular'};n.fontSize=size;n.characters=str;n.fills=[paint(color)];n.textAutoResize='WIDTH_AND_HEIGHT';parent.appendChild(n);n.x=x;n.y=y;return n;
}
function vector(parent,name,points,color,width=1,opacity=1,filled=false){
 const d='M '+points.slice(0,2).join(' ')+' '+points.slice(2).reduce((s,v,i)=>s+(i%2===0?' L ':' ')+v,'')+(filled?' Z':'');
 const svg='<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080" viewBox="0 0 1920 1080"><path d="'+d+'" fill="'+(filled?color:'none')+'" stroke="'+(filled?'none':color)+'" stroke-width="'+width+'" opacity="'+opacity+'"/></svg>';
 const n=track(figma.createNodeFromSvg(svg));n.name=name;parent.appendChild(n);n.x=0;n.y=0;created.push(...n.findAll(()=>true).map(n=>n.id));return n;
}
function node(parent,n,g){
 if(n.type==='text')return label(parent,n.name,n.text,n.x,n.y,n.size,n.color,n.font);
 if(n.type==='rect'){let r=track(figma.createRectangle());r.name=n.name;r.resize(n.w,n.h);r.fills=[paint(n.color)];r.opacity=n.opacity??1;parent.appendChild(r);r.x=n.x;r.y=n.y;return r;}
 if(n.type==='arc'){
  let points=[];let count=Math.max(2,Math.ceil(Math.abs(n.end-n.start)/3));for(let i=0;i<=count;i++){let a=(n.start+(n.end-n.start)*i/count)*Math.PI/180;points.push(n.x+n.w/2+Math.cos(a)*n.w/2,n.y+n.h/2+Math.sin(a)*n.h/2);}
  return vector(parent,n.name,points,n.color,n.stroke,n.opacity??1);
 }
 return vector(parent,n.name,n.points,n.color,n.stroke,n.opacity??1,n.type==='polygon');
}
const section=track(figma.createSection());section.name='ON YOUR MARK / SENSELINK';section.x=200;section.y=200;section.resizeWithoutConstraints(4100,2840);
const masters={};let index=0;
for(const g of layout.groups){
 const c=track(figma.createComponent());c.name='HUD / '+g.name;c.description='1920 × 1080 Unity UGUI / '+g.name+'. Partner telemetry stays crossed until mutual consent or objective-gated return.';c.resize(g.w,g.h);c.fills=[];c.clipsContent=false;section.appendChild(c);c.x=40+(index%6)*660;c.y=2490+Math.floor(index/6)*350;
 for(const n of g.nodes)node(c,n,g);masters[g.name]=c;index++;
}
const cloneVariant=(base,name,changes)=>{const c=track(masters[base].clone());c.name='HUD / '+name;section.appendChild(c);c.x=40+(index%6)*660;c.y=2490+Math.floor(index/6)*350;created.push(...c.findAll(()=>true).map(n=>n.id));for(const [name,value]of Object.entries(changes)){const n=c.findOne(n=>n.type==='TEXT'&&n.name===name);if(n)n.characters=value;}masters[name]=c;index++;return c;};
cloneVariant('Vitals','VitalsCritical',{'Health number':'12','Health state':'SUPPORT REQUIRED','Pulse value':'142 BPM'});
cloneVariant('Vitals','VitalsAwakened',{'Owner':'01 / BIOMETRICS  •  YOU','CallsSign':'VANGUARD','Health number':'92','Your vitals hidden':'YOUR VITALS  /  AUTONOMY RESTORED'});
// Keep native gauge geometry consistent with the state-specific values.
for(const [key,value,color] of [['VitalsCritical',.12,'#FF4E59'],['VitalsAwakened',.92,'#65F8DF']]){
 const component=masters[key],group=layout.groups.find(g=>g.name==='Vitals');
 const arc=group.nodes.find(n=>n.name==='Health arc');
 component.findOne(n=>n.name==='Health arc')?.remove();
 node(component,{...arc,end:arc.start+260*value,color},group);
}
cloneVariant('Channel0','ChannelAwakened',{'Ownership':'YOU  ←  RETURNED','Action':'KILL → DASH RESET'});
cloneVariant('Channel0','ChannelPending',{'Action':'AWAITING CONSENT'});
function background(frame){
 const bg=track(figma.createFrame());bg.name='Relay chamber / editable spatial sketch';bg.resize(1920,1080);frame.appendChild(bg);bg.fills=[{type:'GRADIENT_LINEAR',gradientTransform:[[0,1,0],[-1,0,1]],gradientStops:[{position:0,color:{...rgb('#071418'),a:1}},{position:.5,color:{...rgb('#153C40'),a:1}},{position:1,color:{...rgb('#071416'),a:1}}]}];
 const rays=[[0,0,960,500],[1920,0,960,500],[0,1080,960,500],[1920,1080,960,500]];
 rays.forEach((p,i)=>vector(bg,'Perspective '+i,p,'#355B5C',3,.4));
 for(let i=0;i<7;i++){
  const k=Math.pow(.70,i),l=960-1260*k,r=960+1260*k,t=500-800*k,b=500+670*k;
  vector(bg,'Chamber rib '+i,[l,b,l,t,r,t,r,b],'#0B1D22',Math.max(3,34*k),1);
  vector(bg,'Ion contour '+i,[l+9,b,l+9,t+9,r-9,t+9,r-9,b],'#5BA99A',Math.max(1,3*k),.5);
  vector(bg,'Floor seam '+i,[l,b,r,b],'#49706A',1,.3);
 }
 vector(bg,'Left machinery',[0,580,525,477,630,505,630,724,0,926],'#0A1B21',0,1,true);
 vector(bg,'Right machinery',[1920,580,1395,477,1290,505,1290,724,1920,926],'#0A1B21',0,1,true);
 vector(bg,'Left machinery seam',[0,580,525,477,630,505,630,524,0,651],'#3A605F',2,.7);
 vector(bg,'Right machinery seam',[1920,580,1395,477,1290,505,1290,524,1920,651],'#3A605F',2,.7);
 for(let i=0;i<5;i++){vector(bg,'Service grooves '+i,[145,644+i*24,476,576+i*12],'#294443',6,.7);vector(bg,'Service grooves right '+i,[1775,644+i*24,1444,576+i*12],'#294443',6,.7);}
 vector(bg,'Left floor guide',[450,1080,878,546],'#72E6BB',2,.5);vector(bg,'Right floor guide',[1470,1080,1042,546],'#72E6BB',2,.5);
 const ring=track(figma.createEllipse());ring.name='Relay core';ring.resize(216,216);ring.x=852;ring.y=360;ring.fills=[];ring.strokes=[solid('#5A9E8D',.5)];ring.strokeWeight=10;bg.appendChild(ring);
 const portal=track(figma.createRectangle());portal.name='Warm exit';portal.resize(40,92);portal.x=940;portal.y=440;portal.fills=[solid('#C48C58',.55)];bg.appendChild(portal);
 vector(bg,'Partner silhouette',[1162,626,1162,535,1152,526,1158,498,1185,488,1209,498,1214,526,1206,534,1207,626,1191,626,1187,564,1180,564,1178,626],'#101D23',1,1,true);
 vector(bg,'Partner visor',[1165,510,1201,510],'#9FFFE0',4,1);
 return bg;
}
const states=['field','request','critical','awakened'];const frames=[];
for(let i=0;i<states.length;i++){
 const state=states[i],f=track(figma.createFrame());f.name=['01 / CROSSED SENSES','02 / MUTUAL CONSENT','03 / PARTNER IN CRISIS','04 / AUTONOMY RESTORED'][i];f.resize(1920,1080);f.fills=[solid('#08191D')];f.clipsContent=true;section.appendChild(f);f.x=40+(i%2)*2020;f.y=130+Math.floor(i/2)*1180;background(f);
 for(const g of layout.groups){if(g.state!=='all'&&g.state!==state)continue;let key=g.name;if(key==='Vitals'&&state==='critical')key='VitalsCritical';if(key==='Vitals'&&state==='awakened')key='VitalsAwakened';if(key==='Channel0'&&state==='awakened')key='ChannelAwakened';if(key==='Channel0'&&state==='request')key='ChannelPending';const n=track(masters[key].createInstance());f.appendChild(n);n.x=g.x;n.y=g.y;}
 if(state==='critical') {const v=track(figma.createRectangle());v.name='Danger edge vignette';v.resize(1920,1080);v.fills=[];v.strokes=[solid('#FF4E59',.3)];v.strokeWeight=10;f.appendChild(v);}
 frames.push({id:f.id,name:f.name,width:f.width,height:f.height});
}
const heading=track(figma.createAutoLayout('HORIZONTAL',{name:'Concept title',itemSpacing:38}));section.appendChild(heading);heading.x=40;heading.y=30;heading.fills=[];
label(heading,'Title','SENSELINK',0,0,48,'#E6F3EE','display');label(heading,'Subtitle','ON YOUR MARK  /  16:9 COOPERATIVE HUD',0,0,17,'#65F8DF');
section.resizeWithoutConstraints(4100,2490+Math.ceil(index/6)*350+80);
return {sectionId:section.id,frames,componentCount:index,createdNodeIds:created,variableIds:Object.values(variables).map(v=>v.id),collectionId:collection.id};
