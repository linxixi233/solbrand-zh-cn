import fs from 'node:fs';
import path from 'node:path';
import {resource} from './inspect-settings.mjs';
process.chdir(path.resolve(import.meta.dirname,'..'));
const index=JSON.parse(fs.readFileSync('analysis/pck-index.json'));
const output=[],failures=[];
for(const n of Object.keys(index).filter(n=>n.startsWith('Data/')&&n.endsWith('.tres.remap')).sort()){
 const target=/path="res:\/\/([^"]+)"/.exec(fs.readFileSync('analysis/extracted/'+n,'utf8'))?.[1];
 if(!target)continue;
 try{const parsed=resource(fs.readFileSync('analysis/extracted/'+target));output.push({name:n,target,...parsed});}
 catch(e){failures.push({name:n,error:e.message});}
}
fs.writeFileSync('analysis/coverage-data.json',JSON.stringify(output,null,2));
fs.writeFileSync('analysis/coverage-failures.json',JSON.stringify(failures,null,2));
console.log(JSON.stringify(output.filter(x=>/article_1|DennerDestroyer.*agent|Tugboat.*agent|snake_key|notes|rune|transcript/i.test(x.name)).slice(0,12)));
console.log(JSON.stringify({resources:output.length,failures:failures.length}));
const scenes=[],sceneFailures=[];
for(const n of Object.keys(index).filter(n=>n.endsWith('.tscn.remap')).sort()){
 const target=/path="res:\/\/([^"]+)"/.exec(fs.readFileSync('analysis/extracted/'+n,'utf8'))?.[1];
 if(!target)continue;
 try{scenes.push({name:n,target,...resource(fs.readFileSync('analysis/extracted/'+target))});}
 catch(e){sceneFailures.push({name:n,error:e.message});}
}
fs.writeFileSync('analysis/coverage-scenes.json',JSON.stringify(scenes,null,2));
fs.writeFileSync('analysis/coverage-scene-failures.json',JSON.stringify(sceneFailures,null,2));
console.log(JSON.stringify({scenes:scenes.length,failures:sceneFailures.slice(0,6)}));
