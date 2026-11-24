import {resource} from './inspect-settings.mjs';
import {rewriteResource} from './localize-resources.mjs';
export const isVisibleProperty=k=>/^(?:text|Text|Title|Description|Notice|Origin|TitleText|TypeText|PrimaryText|SecondaryText|TranslationText|LabelText|RegionName|DisplayName|tooltip_text|placeholder_text)$/.test(k);
export function sceneNodes(data){
 const b=data.internals.at(-1)?.properties._bundled;if(!b)return [];
 const names=b['"names"'],variants=b['"variants"'],nodes=b['"nodes"'];let at=0;const result=[];
 for(let i=0;i<b['"node_count"'];i++){
  const parent=nodes[at++],owner=nodes[at++],type=nodes[at++],name=nodes[at++]&0x3ffff,instance=nodes[at++],count=nodes[at++],properties=[];
  for(let j=0;j<count;j++){const key=names[nodes[at++]&0x3fffffff],index=nodes[at++];properties.push({key,index,value:variants[index]});}
  const groups=nodes[at++];at+=groups;result.push({parent,owner,type:names[type]??type,name:names[name],instance,properties});
 }
 if(at!==nodes.length)throw Error('Scene node table length mismatch');
 return result;
}
export function localizeScene(input,dictionary){
 const data=resource(input,{track:true}),nodes=sceneNodes(data),references=new Map();
 for(const node of nodes)for(const property of node.properties){const refs=references.get(property.index)??[];refs.push(property);references.set(property.index,refs);}
 const translations=new Map();
 for(const [i,refs]of references)if(refs.every(x=>isVisibleProperty(x.key))&&typeof refs[0].value==='string'&&Object.hasOwn(dictionary,refs[0].value))
  translations.set(i,dictionary[refs[0].value]);
 const edits=data.binary.spans.filter(x=>/^_bundled\/variants\[\d+\]$/.test(x.key)&&translations.has(Number(/\[(\d+)\]/.exec(x.key)[1]))).map(x=>{
  const value=translations.get(Number(/\[(\d+)\]/.exec(x.key)[1])),text=Buffer.from(value+'\0'),length=Buffer.alloc(4);length.writeUInt32LE(text.length);
  return {...x,bytes:Buffer.concat([length,text])};
 }).sort((a,b)=>a.start-b.start);
 const expected={type:data.type,scriptClass:data.scriptClass,externals:data.externals,internals:data.internals.map(({offset,...x})=>structuredClone(x))};
 const bundle=expected.internals.at(-1)?.properties._bundled;
 if(bundle)for(const[i,t]of translations)bundle['"variants"'][i]=t;
 return rewriteResource(input,data,edits,expected);
}
