// Revision 4: remove the weapon enclosure while preserving native instances.
// Inject reserves (shared layout group) and masterId (existing Reserves component).
await figma.setCurrentPageAsync(await figma.getNodeByIdAsync('0:1'));
const master=await figma.getNodeByIdAsync(masterId);
const removed=[],created=[],mutated=[master.id];
for(const child of [...master.children])if(['Glass','Contour','Signal','Active weapon accent'].includes(child.name)){removed.push(child.id);child.remove();}
master.fills=[];
const n=reserves.nodes.find(n=>n.name==='Active weapon accent');
const localVars=await figma.variables.getLocalVariablesAsync('COLOR');
const cyan=localVars.find(v=>v.name==='cyan');
const color={r:101/255,g:248/255,b:223/255};
const line=figma.createRectangle();master.appendChild(line);created.push(line.id);
line.name=n.name;line.resize(n.w,n.h);line.x=n.x;line.y=n.y;
const fill={type:'SOLID',color,opacity:n.opacity};line.fills=[cyan?figma.variables.setBoundVariableForPaint(fill,'color',cyan):fill];
line.effects=[{type:'DROP_SHADOW',color:{...color,a:.5},offset:{x:0,y:0},radius:7,spread:0,visible:true,blendMode:'SCREEN'}];
master.description='Floating weapon HUD: rifle, blade, grenade and ammunition. No backing panel or enclosing border. Shared Unity geometry.';
const frames=['9:577','9:994','9:1423','9:1852','15:1538','15:1628','18:615','18:814','18:1013'];
for(const id of frames){const f=await figma.getNodeByIdAsync(id),r=f.children.find(n=>n.name==='Reserves');if(r.children.some(n=>['Glass','Contour','Signal'].includes(n.name)))throw new Error('Weapon enclosure remains in '+id);}
return {createdNodeIds:created,mutatedNodeIds:mutated,removedNodeIds:removed,updatedFrames:frames.length,weaponBackdrop:false};
