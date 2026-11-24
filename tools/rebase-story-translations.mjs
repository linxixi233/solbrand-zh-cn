import fs from 'node:fs';
import path from 'node:path';
const [oldFile,newFile,output]=process.argv.slice(2);
if(!output)throw Error('Usage: node tools/rebase-story-translations.mjs <old-story.json> <new-story.json> <output-dir>');
const old=JSON.parse(fs.readFileSync(oldFile)).root.at(-1),current=JSON.parse(fs.readFileSync(newFile)).root.at(-1);
const sources=v=>{const a=[];const walk=x=>{if(typeof x==='string'&&x.startsWith('^')){if(!a.includes(x.slice(1)))a.push(x.slice(1));}else if(Array.isArray(x))x.forEach(walk);else if(x&&typeof x==='object')Object.values(x).forEach(walk);};walk(v);return a;};
const normalize=s=>s.trim().replace(/^[()]|[()]$/g,'').replace(/[.!…]+$/g,'').replace(/:/g,'').toLowerCase();
const report={exact:0,punctuation:[],unmatched:[],conflicts:[]};
fs.mkdirSync(path.join(output,'story'),{recursive:true});
function rebase(knot,entries){
 const a=sources(old[knot]),b=sources(current[knot]),result={};
 for(const [i,zh]of Object.entries(entries)){
  const source=a[Number(i)];if(source===undefined)throw Error(`Invalid baseline index ${knot}:${i}`);
  let index=b.indexOf(source);
  if(index<0){const matches=b.map((s,j)=>({s,j})).filter(x=>normalize(x.s)===normalize(source));if(matches.length===1){index=matches[0].j;report.punctuation.push({knot,oldIndex:Number(i),newIndex:index,old:source,current:b[index],zh});}}
  if(index<0){report.unmatched.push({knot,index:Number(i),source,zh});continue;}
  if(result[index]&&result[index]!==zh)report.conflicts.push({knot,index,source,zh,other:result[index]});
  result[index]=zh;if(source===b[index])report.exact++;
 }
 return result;
}
fs.writeFileSync(path.join(output,'tutorial.zh.json'),JSON.stringify(rebase('Tutorial',JSON.parse(fs.readFileSync('translations/tutorial.zh.json'))),null,2)+'\n');
for(const file of fs.readdirSync('translations/story').filter(n=>n.endsWith('.json'))){
 const data=JSON.parse(fs.readFileSync(path.join('translations/story',file))),result={};
 for(const[k,entries]of Object.entries(data))result[k]=rebase(k,entries);
 fs.writeFileSync(path.join(output,'story',file),JSON.stringify(result,null,2)+'\n');
}
const overrideFile=path.join(output,'rebase-overrides.zh.json');
if(fs.existsSync(overrideFile)){
 const overrides=JSON.parse(fs.readFileSync(overrideFile));report.manuallyReviewed=[];
 for(const entry of overrides){
  const index=sources(current[entry.knot]).indexOf(entry.source);if(index<0)throw Error('Override source missing: '+entry.source);
  const file=path.join(output,'story',entry.file),entries=JSON.parse(fs.readFileSync(file));(entries[entry.knot]??={})[index]=entry.zh;
  fs.writeFileSync(file,JSON.stringify(entries,null,2)+'\n');
  for(const row of report.unmatched.filter(x=>x.knot===entry.knot&&x.source===entry.previousSource))report.manuallyReviewed.push(row);
  report.unmatched=report.unmatched.filter(x=>!(x.knot===entry.knot&&x.source===entry.previousSource));
 }
 report.newTranslations=overrides.length;
}
fs.writeFileSync(path.join(output,'rebase-report.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({exact:report.exact,punctuation:report.punctuation.length,unmatched:report.unmatched,conflicts:report.conflicts},null,2));
