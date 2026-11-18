import fs from 'node:fs';
const colors={primary:'#B7A6F6',dim:'#716A91',white:'#F3EEDD',accent:'#D5EF86',danger:'#F78E72',ink:'#141322',panel:'#252239',muted:'#B7B0C8'};
const groups=[];
const circle=(x,y,r,start=0,end=360)=>Array.from({length:33},(_,i)=>{const a=(start+(end-start)*i/32)*Math.PI/180;return [x+Math.cos(a)*r,y+Math.sin(a)*r];}).flat();
const rounded=(x,y,w,h,r)=>[[x+w-r,y+r,-90],[x+w-r,y+h-r,0],[x+r,y+h-r,90],[x+r,y+r,180]].flatMap(([cx,cy,start])=>Array.from({length:7},(_,i)=>{const a=(start+i*15)*Math.PI/180;return [cx+Math.cos(a)*r,cy+Math.sin(a)*r];}).flat());
// Shared 24-unit paths become editable Figma vectors and native UGUI meshes.
const icons=Object.entries({
 heart:[[2,8,4,4,8,3,12,7,16,3,20,4,22,8,21,13,12,21,3,13,2,8]],
 map:[[2,5,8,2,16,5,22,2,22,19,16,22,8,19,2,22,2,5],[8,2,8,19],[16,5,16,22]],
 ammo:[[3,20,3,8,6,3,9,8,9,20,3,20],[15,20,15,8,18,3,21,8,21,20,15,20],[3,16,9,16],[15,16,21,16]],
 link:[[3,8,20,8,16,4],[21,16,4,16,8,20]],
 support:[[12,2,21,5,20,14,17,19,12,22,7,19,4,14,3,5,12,2],[8,11,16,11],[12,7,12,15]],
 shield:[[12,2,21,5,20,14,17,19,12,22,7,19,4,14,3,5,12,2],[12,6,12,17]],
 scan:[circle(12,12,9),circle(12,12,5,0,270),[12,12,19,5],[12,9,12,15],[9,12,15,12]],
 supply:[[3,8,21,8,21,21,3,21,3,8],[2,3,22,3,22,8,2,8,2,3],[9,14,15,14],[12,11,12,17]],
 close:[[6,6,18,18],[18,6,6,18]],
 check:[[5,12,10,17,20,6]],
 wait:[[6,3,18,3,18,7,6,17,6,21,18,21,18,17,6,7,6,3]],
 alert:[[12,2,23,21,1,21,12,2],[12,8,12,14],[12,17,12,18]],
 unlock:[[7,10,7,6,9,3,13,2,17,4],[4,10,20,10,20,22,4,22,4,10],[12,14,12,18]],
 bolt:[[14,1,4,14,11,14,10,23,21,9,14,9,14,1]],
 partner:[circle(8,6,3),circle(17,7,2.5),[2,21,2,16,5,12,11,12,14,16,14,21],[16,13,19,13,22,17,22,21]],
 self:[circle(12,6,3.5),[5,22,5,16,8,12,16,12,19,16,19,22]],
 relay:[[12,2,20,7,20,17,12,22,4,17,4,7,12,2],[12,7,16,10,16,14,12,17,8,14,8,10,12,7]],
 marker:[[12,2,21,11,12,20,3,11,12,2],[8,11,12,15,16,11]]
}).map(([name,paths])=>({name,width:24,height:24,strokes:paths.map(points=>({points}))}));
// Weapon silhouettes retain their own aspect ratio. Convex filled parts keep the
// stock, barrel, magazine, blade and grenade lever readable at HUD scale.
icons.push({name:'rifle',width:100,height:32,strokes:[
 [2,12,16,12,22,18,18,24,2,24], [20,12,52,12,60,17,52,23,20,23],
 [22,9,51,9,55,12,22,12], [53,13,76,13,80,17,76,20,53,20],
 [76,15,94,15,94,18,76,18], [93,13,100,13,100,20,93,20],
 [40,22,52,22,48,32,39,30], [25,22,34,22,32,30,26,30],
 [32,3,49,3,52,5,49,8,32,8,29,5], [34,7,37,7,37,10,34,10], [45,7,48,7,48,10,45,10]
].map(points=>({points,fill:true}))});
icons.push({name:'sword',width:100,height:24,strokes:[
 {points:[28,8,84,8,100,12,84,16,28,16],fill:true},
 {points:[24,3,29,3,31,21,26,21],fill:true},
 {points:[7,9,25,9,25,15,7,15],fill:true},
 {points:[2,7,8,7,8,17,2,17],fill:true}
]});
icons.push({name:'grenade',width:24,height:32,strokes:[
 {points:[8,10,17,10,21,16,21,25,17,31,7,31,3,25,3,17],fill:true},
 {points:[8,6,17,6,17,11,8,11],fill:true},
 {points:[15,5,20,8,23,22],fill:false},
 {points:circle(7,4,3),fill:false}
]});
// Reference-inspired weapon line art: preserve contours and internal divisions
// instead of filling the entire silhouette. Open pin/lever strokes stay open.
for(const weapon of icons.filter(i=>['rifle','sword','grenade'].includes(i.name)))
 for(const stroke of weapon.strokes)if(stroke.fill){stroke.points.push(...stroke.points.slice(0,2));stroke.fill=false;}
function group(name,x,y,w,h,state='all',action=''){const g={name,x,y,w,h,state,action,nodes:[]};groups.push(g);return g;}
const add=(g,n)=>g.nodes.push(n);
const text=(g,name,t,x,y,size=18,color='white',w=80)=>add(g,{name,type:'text',text:t,x,y,w,h:size*1.45,size,color:colors[color],font:'display'});
const rect=(g,name,x,y,w,h,color='primary',opacity=1,glow=false)=>add(g,{name,type:'rect',x,y,w,h,color:colors[color],opacity,glow});
const line=(g,name,points,color='primary',stroke=1,opacity=1,glow=false)=>add(g,{name,type:'line',points,color:colors[color],stroke,opacity,glow});
const arc=(g,name,x,y,r,start,end,stroke=2,color='primary',opacity=1,glow=false)=>add(g,{name,type:'arc',x,y,w:r*2,h:r*2,start,end,stroke,color:colors[color],opacity,glow});
const icon=(g,name,kind,x,y,size=24,color='primary',glow=false)=>add(g,{name,type:'icon',icon:kind,x,y,w:size,h:size,stroke:1.8,color:colors[color],opacity:1,glow});
function panel(g,accent='primary'){
 add(g,{name:'Glass',type:'polygon',points:rounded(0,0,g.w,g.h,12),color:colors.ink,opacity:.82});
 line(g,'Contour',[...rounded(0,0,g.w,g.h,12),g.w-12,0],'dim',1,.4);
 rect(g,'Signal',16,0,24,2,accent,.8,true);
}
function actionButton(name,x,y,w,h,kind,action,state='all'){
 const g=group(name,x,y,w,h,state,action);panel(g);
 icon(g,'Action icon',kind,(w-26)/2,(h-26)/2,26,'white');
 rect(g,'Glow',10,h-2,w-20,2,'primary',.6,true);return g;
}
// Keep the established four-corner composition; use soft instrument housings.
let g=group('Spatial',1632,40,240,250);
add(g,{name:'Map glass',type:'polygon',points:rounded(0,0,240,250,16),color:colors.ink,opacity:.5});
line(g,'Map contour',[...rounded(0,0,240,250,16),224,0],'primary',1.2,.8);
line(g,'Map inner edge',[12,40,12,24,24,12,54,12],'dim',1,.5);
icon(g,'Ownership icon','partner',211,14,17,'primary');
line(g,'North arrow',[217,42,222,32,227,42],'primary',1.5,.9);
for(let i=0;i<5;i++)rect(g,'Connection '+i,24+i*7,20,4,2,'accent',.85);
line(g,'Street west',[22,58,65,58,65,100,30,100,30,166,78,166,78,225],'dim',2,.75);
line(g,'Street east',[94,34,94,82,151,82,151,135,215,135,215,207,174,207,174,231],'dim',2,.75);
line(g,'Street center',[27,126,116,126,116,166,200,166],'dim',3,.65);
line(g,'District north',[121,36,187,36,187,62,121,62,121,36],'dim',1,.5);
line(g,'District south',[100,194,143,194,143,224,100,224,100,194],'dim',1,.5);
for(let y=121;y<=185;y+=11)line(g,'Route north '+y,[117,y,117,y+5],'accent',2,.95,true);
for(let x=117;x<=172;x+=11)line(g,'Route east '+x,[x,116,x+5,116],'accent',2,.95,true);
for(let y=61;y<=105;y+=11)line(g,'Route target '+y,[177,y,177,y+5],'accent',2,.95,true);
line(g,'Ally',[110,198,117,183,124,198,117,194,110,198],'primary',2,1,true);
line(g,'Destination',[177,46,184,53,177,60,170,53,177,46],'accent',1.6,1,true);
rect(g,'Contact',53,143,4,4,'danger',1,true);
line(g,'Map footer',[94,242,146,242],'primary',1,.8);
g=group('Objective',1568,328,304,72);
icon(g,'Mission icon','relay',0,0,20,'accent');
line(g,'Mission header',[30,10,178,10],'dim',1,.7);
icon(g,'Destination icon','marker',20,37,23,'accent',true);
line(g,'Progress track',[62,49,216,49],'accent',1,.3);
for(let i=0;i<3;i++){
 const x=72+i*58;
 add(g,{name:'Objective '+i,type:'polygon',points:circle(x,49,4),color:colors[i<2?'accent':'dim'],opacity:i<2?.95:.4,glow:i<2});
}
g=group('Reticle',944,534,32,12);
line(g,'Left',[0,6,10,6],'primary',1.2,.7);line(g,'Right',[22,6,32,6],'primary',1.2,.7);rect(g,'Aim dot',15,5,2,2,'primary',.9);
g=group('AllyMarker',1080,476,56,28);icon(g,'Partner marker','marker',0,0,22,'primary',true);text(g,'Distance','23',29,3,16,'muted',28);
g=group('Vitals',48,36,428,76);
line(g,'Vitals badge',[2,12,2,43,10,43],'primary',2,.85);
text(g,'Health number','78',12,9,34,'white',46);
icon(g,'Health icon','heart',64,19,17,'primary');
rect(g,'Link track',88,8,280,2,'dim',.4);
rect(g,'Link rail',88,8,245,2,'primary',.8,true);
rect(g,'Health track',88,18,280,12,'dim',.25);
rect(g,'Health bar',88,18,218.4,12,'primary',1);
line(g,'Health cap',[376,18,376,30],'primary',1.4,.8);
for(let i=0;i<8;i++)rect(g,'Link segment '+i,88+i*24,40,18,3,'primary',i<6?.75:.2);
line(g,'Status rail',[88,0,120,0],'accent',1,.8);
icon(g,'Ownership icon','partner',400,18,20);
g=group('Reserves',1552,960,320,80);
text(g,'Ammo number','06',0,16,38,'white',56);
icon(g,'Primary weapon','rifle',68,4,32,'white',true);Object.assign(g.nodes.at(-1),{w:160,h:51.2,stroke:1.25});
icon(g,'Melee weapon','sword',68,58,24,'white');Object.assign(g.nodes.at(-1),{w:80,h:19.2,stroke:1.25,opacity:.8});
icon(g,'Grenade','grenade',248,45,24,'white');Object.assign(g.nodes.at(-1),{w:21,h:28,stroke:1.25,opacity:.8});
icon(g,'Ownership icon','partner',296,22,20);
rect(g,'Active weapon accent',68,55,48,2,'accent',.7,true);
for(let i=0;i<8;i++)rect(g,'Cell '+i,i*7,65,4,3,i<6?'primary':'dim',i<6?.85:.25,i<6);
// Quick actions sit in the bottom-left corner; no center-bottom controls.
function quickSlot(name,x,kind,action){
 const g=group(name,x,948,64,76,'all',action);
 add(g,{name:'Slot shade',type:'polygon',points:rounded(0,6,64,60,14),color:colors.ink,opacity:.2});
 line(g,'Slot contour',[...rounded(0,6,64,60,14),50,6],'primary',1.3,.85);
 rect(g,'Slot latch',26,6,12,2,'accent',.8);
 icon(g,'Action icon',kind,17,20,30,'white');
 rect(g,'Glow',22,66,20,2,'primary',.6,true);
 return g;
}
quickSlot('ToggleChannels',48,'link','channels');quickSlot('ToggleSupport',128,'support','support');
quickSlot('QuickShield',208,'shield','send0');quickSlot('QuickScan',288,'scan','send1');
g=group('Channels',48,768,304,152,'channels');panel(g);icon(g,'Tray icon','link',20,12,24,'primary');
g=group('SupportWheel',48,768,304,152,'support');panel(g);icon(g,'Tray icon','support',20,12,24,'primary');
for(let i=0;i<3;i++){
 g=actionButton('Channel'+i,68+i*96,836,72,64,['heart','map','ammo'][i],'exchange'+i,'channels');
 const glyph=g.nodes.find(n=>n.name==='Action icon');Object.assign(glyph,{x:20,y:16,w:32,h:32});
 actionButton('Support'+i,68+i*96,836,72,64,['shield','scan','supply'][i],'send'+i,'support');
}
actionButton('CloseTray',300,772,44,44,'close','close','tray');
g=group('Exchange',48,142,280,64,'request');panel(g,'accent');
icon(g,'Channel icon','heart',16,18,28,'white');icon(g,'Exchange icon','link',57,22,20,'accent');icon(g,'Local ready','check',96,21,22);icon(g,'Partner waiting','wait',143,21,22,'accent');rect(g,'Request glow',16,61,164,2,'accent',.85,true);
actionButton('CancelExchange',276,152,44,44,'close','cancel','request');
g=group('Critical',48,124,108,48,'critical','support');
icon(g,'Alert icon','alert',0,11,24,'danger',true);icon(g,'Support icon','shield',54,13,22,'white');rect(g,'Glow',0,46,94,2,'danger',.6,true);
g=group('Awakened',48,142,192,56,'awakened');
icon(g,'Unlock icon','unlock',0,15,26,'accent',true);icon(g,'Restored channel','heart',52,16,24,'white');icon(g,'Awake power','bolt',104,16,24,'accent');
g=group('Toast',368,960,52,44,'toast');icon(g,'Success icon','check',13,9,26,'primary',true);
const layout={width:1920,height:1080,revision:7,colors,icons,groups};
for(const path of ['Assets/garam/UI/Resources/HudLayout.json','Design/hud-layout.json'])fs.writeFileSync(path,JSON.stringify(layout,null,2));
console.log(JSON.stringify({groups:groups.length,fieldText:groups.filter(g=>g.state==='all').flatMap(g=>g.nodes).filter(n=>n.type==='text').length,nodes:groups.reduce((n,g)=>n+g.nodes.length,0),icons:icons.length}));
