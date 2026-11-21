import fs from 'node:fs';
import path from 'node:path';
process.chdir(path.resolve(import.meta.dirname,'..'));
const manifest=JSON.parse(fs.readFileSync('dist/manifest.json','utf8'));
const rows=JSON.parse(fs.readFileSync('analysis/translation/bilingual.json','utf8'));
const lines=[`Solbrand Demo v${manifest.version} 译文对照（首轮初译）`,'','本文件按脚本片段列出原文与译文；相邻片段在游戏里可能拼接成一句。','片段编号用于维护，不是游戏中的行号。本版未进行实机通读、排版和分支验证。',''];
let previous='';
for(const row of rows){if(row.knot!==previous){lines.push('','======== '+row.knot+' ========','');previous=row.knot;}lines.push('['+row.index+'] 原文：'+row.en,'译文：'+row.zh,'');}
for(const [file,title]of [['ui','界面与名称'],['manual','游戏手册'],['prose','百科、遗物、地点及任务资源']]){
 lines.push('','======== '+title+' ========','');
 for(const [source,target]of Object.entries(JSON.parse(fs.readFileSync('translations/'+file+'.zh.json','utf8'))))lines.push('原文：'+source,'译文：'+target,'');
}
fs.writeFileSync('译文对照.txt',lines.join('\r\n'));
const terms=JSON.parse(fs.readFileSync('translations/terminology.zh.json','utf8'));
fs.writeFileSync('术语表.txt',['Solbrand Demo 暂定术语表（v'+manifest.version+'）','','人物键、事件键和资源标识保留英文；本表用于显示文本与译文。','',...Object.entries(terms).map(([en,zh])=>en+'：'+zh),'','古诺尔斯语词根、诗句与用于解谜的拼写，按上下文保留原文。','名称和拼接句仍待实机通读校对。',''].join('\r\n'));
console.log(JSON.stringify({bilingualFragments:rows.length,reviewBytes:fs.statSync('译文对照.txt').size}));
