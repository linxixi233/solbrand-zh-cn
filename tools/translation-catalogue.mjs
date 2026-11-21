import fs from 'node:fs';
import path from 'node:path';
process.chdir(path.resolve(import.meta.dirname,'..'));
const root=JSON.parse(fs.readFileSync('analysis/story.json','utf8')).root.at(-1);
const catalogue={};
const controlTags={};
for(const [k,v]of Object.entries(root)){
 const strings=[];const walk=x=>{if(typeof x==='string'&&x.startsWith('^')){if(!strings.includes(x.slice(1)))strings.push(x.slice(1));}else if(Array.isArray(x))x.forEach(walk);else if(x&&typeof x==='object')Object.values(x).forEach(walk);};walk(v);catalogue[k]=strings;
 const tags=new Set();const scan=(x,tag=false)=>{if(Array.isArray(x)){for(const item of x){if(item==='#')tag=true;else if(item==='/#')tag=false;else if(typeof item==='string'&&item.startsWith('^')&&tag)tags.add(item.slice(1));else if(item&&typeof item==='object')scan(item,tag);}}else if(x&&typeof x==='object')Object.values(x).forEach(item=>scan(item,tag));};scan(v);controlTags[k]=tags;
}
const translations={Tutorial:JSON.parse(fs.readFileSync('translations/tutorial.zh.json','utf8'))};
for(const file of fs.readdirSync('translations/story').filter(n=>n.endsWith('.json')&&!n.startsWith('00-')).sort())for(const [k,v]of Object.entries(JSON.parse(fs.readFileSync('translations/story/'+file,'utf8'))))translations[k]={...translations[k],...v};
const memory=new Map();
for(const[k,entries]of Object.entries(translations))for(const[i,target]of Object.entries(entries)){
 const source=catalogue[k][i];if(!memory.has(source))memory.set(source,new Set());memory.get(source).add(target);
}
const shared={};
for(const[k,strings]of Object.entries(catalogue)){
 if(['Tutorial','Debug','global decl'].includes(k)||/Debug|Playtest/.test(k))continue;
 strings.forEach((s,i)=>{const options=memory.get(s);if(!controlTags[k].has(s)&&!translations[k]?.[i]&&s.length>=11&&options?.size===1&&/[\u3400-\u9fff]/.test([...options][0])){(shared[k]??={})[i]=[...options][0];}});
}
fs.writeFileSync('translations/story/00-shared.zh.json',JSON.stringify(shared,null,2));
for(const[k,entries]of Object.entries(shared))translations[k]={...entries,...translations[k]};
const pending={};
for(const[k,strings]of Object.entries(catalogue))if(!/Debug|Playtest/.test(k)&&k!=='global decl'){
 const list=strings.map((s,i)=>({i,s})).filter(x=>!translations[k]?.[x.i]&&/[A-Za-z]/.test(x.s)&&!/^\s*$|^[a-z0-9_]+$|^[a-z]+:|^(?:event|res|uid):\/\/|^\[\/?[^\]]*\]$/.test(x.s));
 if(list.length)pending[k]=list;
}
fs.mkdirSync('analysis/translation',{recursive:true});
fs.writeFileSync('analysis/translation/pending.json',JSON.stringify(pending,null,2));
const rows=[];
for(const[k,entries]of Object.entries(translations))for(const[i,zh]of Object.entries(entries))rows.push({knot:k,index:Number(i),en:catalogue[k][i],zh});
fs.writeFileSync('analysis/translation/bilingual.json',JSON.stringify(rows,null,2));
const counts={translatedFragments:rows.length,distinctEnglishSources:new Set(rows.map(x=>x.en)).size,sharedEntries:Object.values(shared).reduce((a,v)=>a+Object.keys(v).length,0),pendingCandidates:Object.values(pending).reduce((a,v)=>a+v.length,0)};
fs.writeFileSync('analysis/translation/progress.json',JSON.stringify(counts,null,2));console.log(JSON.stringify(counts));
