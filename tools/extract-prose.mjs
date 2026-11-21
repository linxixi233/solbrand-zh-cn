import fs from 'node:fs';
import path from 'node:path';
import {resource} from './inspect-settings.mjs';
const root=path.resolve(import.meta.dirname,'..');process.chdir(root);
const index=JSON.parse(fs.readFileSync('analysis/pck-index.json','utf8'));
const strings=new Map(),failures=[];
for(const n of Object.keys(index).filter(n=>n.startsWith('Data/')&&n.endsWith('.tres.remap')).sort()){
 const remap=fs.readFileSync('analysis/extracted/'+n,'utf8');
 const target=/path="res:\/\/([^"]+)"/.exec(remap)?.[1];if(!target)continue;
 try{const data=resource(fs.readFileSync('analysis/extracted/'+target));for(const item of data.internals)for(const[key,text]of Object.entries(item.properties)){
  if(!/Title|Description|Hint|Text|Message|Notes|Label|^Name$/.test(key)||typeof text!=='string'||!/[A-Za-z]/.test(text)||/^(?:res|uid|local):\/\/|^[a-z0-9_]+$/.test(text))continue;
  if(!strings.has(text))strings.set(text,{en:text,contexts:[]});strings.get(text).contexts.push({resource:n,property:key,class:data.scriptClass});
 }}catch(e){failures.push({resource:n,error:e.message});}
}
const entries=Array.from(strings.values()).map((x,i)=>({id:i,...x}));
fs.writeFileSync('analysis/prose-source.json',JSON.stringify(entries,null,2));
fs.writeFileSync('analysis/prose-failures.json',JSON.stringify(failures,null,2));
console.log(JSON.stringify({entries:entries.length,words:entries.reduce((a,x)=>a+x.en.split(/\s+/).length,0),failures:failures.length}));
