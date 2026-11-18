// Inject the exact revision-2 IDs as design, plus shared layout.
await figma.setCurrentPageAsync(await figma.getNodeByIdAsync('0:1'));
await Promise.all([{family:'Rajdhani',style:'SemiBold'},{family:'IBM Plex Mono',style:'Regular'}].map(f=>figma.loadFontAsync(f)));
const created=[],mutated=[];
const library=await figma.getNodeByIdAsync(design.library);
const transition={type:'SMART_ANIMATE',duration:.16,easing:{type:'EASE_OUT'}};
const reaction=(trigger,id,navigation='NAVIGATE')=>({trigger:{type:trigger},actions:[{type:'NODE',destinationId:id,navigation,transition,preserveScrollPosition:false}]});
const hoverByAction={};let i=0;
for(const g of layout.groups.filter(g=>g.action)){
 const c=await figma.getNodeByIdAsync(design.masters[g.name]);
 if(c.parent.type==='COMPONENT_SET')throw new Error('Hover variants already exist; inspect before retry.');
 const h=c.clone();library.appendChild(h);created.push(h.id,...h.findAll(()=>true).map(n=>n.id));
 const highlight=figma.createRectangle();h.insertChild(0,highlight);highlight.name='Hover bloom';highlight.resize(g.w,g.h);highlight.fills=[{type:'SOLID',color:{r:.25,g:.95,b:.8},opacity:.12}];highlight.effects=[{type:'DROP_SHADOW',color:{r:.25,g:1,b:.8,a:.38},offset:{x:0,y:0},radius:14,visible:true,blendMode:'SCREEN'}];created.push(highlight.id);
 c.name='State=Default';h.name='State=Hover';
 const set=figma.combineAsVariants([c,h],library);set.name='Interactive / '+g.name;set.x=40+(i%3)*780;set.y=2760+Math.floor(i/3)*340;set.resize(Math.max(g.w*2+60,460),g.h+40);c.x=20;c.y=20;h.x=g.w+40;h.y=20;i++;created.push(set.id);mutated.push(c.id);
 await c.setReactionsAsync([reaction('ON_HOVER',h.id,'CHANGE_TO')]);
 hoverByAction[g.name]=h.id;
}
const routes={channels:design.frames.channels,support:design.frames.support,close:design.frames.field,cancel:design.frames.field,exchange0:design.frames.request,exchange1:design.frames.request,exchange2:design.frames.request,send0:design.frames.field,send1:design.frames.field,send2:design.frames.field};
let links=0;
for(const [state,ids]of Object.entries(design.instances))for(const g of layout.groups.filter(g=>g.action)){
 const id=ids[g.name];if(!id)continue;const n=await figma.getNodeByIdAsync(id);
 let destination=routes[g.action];if(g.action===state)destination=design.frames.field;
 await n.setReactionsAsync([reaction('ON_HOVER',hoverByAction[g.name],'CHANGE_TO'),reaction('ON_CLICK',destination)]);mutated.push(n.id);links++;
}
// Hover variants also retain click routes when reached in presentation mode.
for(const g of layout.groups.filter(g=>g.action)){
 const h=await figma.getNodeByIdAsync(hoverByAction[g.name]);
 await h.setReactionsAsync([reaction('ON_CLICK',routes[g.action])]);mutated.push(h.id);
}
library.resizeWithoutConstraints(2400,2760+Math.ceil(i/3)*340+60);mutated.push(library.id);
const field=await figma.getNodeByIdAsync(design.frames.field);
const descendants=field.findAll(()=>true),counts={};for(const n of descendants)counts[n.type]=(counts[n.type]||0)+1;
const images=descendants.filter(n=>Array.isArray(n.fills)&&n.fills.some(f=>f.type==='IMAGE')).map(n=>({id:n.id,name:n.name,type:n.type,width:n.width,height:n.height}));
const fonts=[...new Set(descendants.filter(n=>n.type==='TEXT').map(n=>n.fontName.family))];
if(fonts.some(f=>!['Rajdhani','IBM Plex Mono'].includes(f)))throw new Error('Unexpected font family.');
return {createdNodeIds:created,mutatedNodeIds:mutated,links,hoverVariants:i,counts,imageFilledNodes:images,fonts,minimap:{x:56,y:48,w:232,h:250},defaultTextCount:counts.TEXT};
