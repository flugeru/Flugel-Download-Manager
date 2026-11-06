// Local deterministic artifact only; never reaches external network.
const http = require('node:http');
const fs = require('node:fs');
const crypto = require('node:crypto');
const path = require('node:path');
const log = path.join(__dirname, 'idm-http-requests.jsonl');
const data = Buffer.alloc(16 * 1024 * 1024);
for (let i = 0; i < data.length; ++i) data[i] = i % 251;
fs.writeFileSync(path.join(__dirname, 'fixture.json'), JSON.stringify({length:data.length,sha256:crypto.createHash('sha256').update(data).digest('hex')},null,2));
const server = http.createServer((req,res) => {
  fs.appendFileSync(log, JSON.stringify({time:new Date().toISOString(),method:req.method,path:req.url,range:req.headers.range,ifRange:req.headers['if-range'],encoding:req.headers['accept-encoding']})+'\n');
  if (req.url === '/redirect') {res.writeHead(302,{Location:'/sample.bin'});res.end();return;}
  if (!['/sample.bin','/no-range.bin'].includes(req.url)) {res.writeHead(404);res.end();return;}
  let start=0,end=data.length-1,status=200;
  const headers={'Content-Type':'application/octet-stream','Accept-Ranges':'bytes',ETag:'"fixture-v1"','Last-Modified':'Wed, 04 Nov 2026 00:00:00 GMT'};
  if(req.headers.range && req.url !== '/no-range.bin') {
    const m=/^bytes=(\d+)-(\d*)$/.exec(req.headers.range);
    if(!m || Number(m[1])>=data.length){res.writeHead(416,{'Content-Range':`bytes */${data.length}`});res.end();return;}
    start=Number(m[1]);end=m[2]?Math.min(Number(m[2]),end):end;status=206;
    headers['Content-Range']=`bytes ${start}-${end}/${data.length}`;
  }
  headers['Content-Length']=end-start+1;
  res.writeHead(status,headers);
  if(req.method==='HEAD'){res.end();return;}
  let position=start;
  const timer=setInterval(()=>{
    if(position>end){clearInterval(timer);res.end();return;}
    const next=Math.min(position+32768,end+1);res.write(data.subarray(position,next));position=next;
  },20);
  res.on('close',()=>clearInterval(timer));
});
server.listen(18743,'127.0.0.1',()=>console.log('IDM controlled fixture http://127.0.0.1:18743/sample.bin'));
