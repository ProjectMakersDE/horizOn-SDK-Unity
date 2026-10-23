#!/usr/bin/env python3
"""Real loopback HTTP + WebSocket metadata fixture, isolated from all hosted data."""
import base64,copy,hashlib,json,os,socket,socketserver,struct,threading,time,uuid
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from mock_http import send_json,report_rejection
U='00000000-0000-4000-8000-000000001014'
C='00000000-0000-4000-8000-000000003014'
E='00000000-0000-4000-8000-000000004014'
T='2026-10-16T12:00:00.000Z';X='2026-10-23T12:00:00.000Z';BASE='/api/v1/app/chat'
lock=threading.RLock();wire_lock=threading.Lock();finished=threading.Event()
state={'revision':1,'rows':[],'receipts':{},'reports':{},'syncs':[],'mutations':[],
       'connections':0,'subscriptions':set(),'allowed':True,'socket':None,'failure':None,
       'bad':False,'delay':False,'event':0,'http':0,'unsupported':False,'failOpen':False,'failReport':False,
       'failAuth':False,'lateFailure':False,'lateClock':False}
LIMITS={'retainedMessagesPerPlayer':0,'retainedMessagesPerChannel':20,'retentionSeconds':604800,'privateRetainedMessagesPerChannel':15,'privateRetentionSeconds':30,'operationWindowSeconds':30,'operationFutureToleranceSeconds':60,
 'messageCharacters':500,'messageUtf8Bytes':2000,'channelsPerAccount':500,'sendsPerAccountMinute':60,
 'playerSendIntervalMillis':3000,'channelSendIntervalMillis':2000,'reportsPerProject':150,
 'receiptCount':0,'reportReceiptCount':0,'wordFilterTerms':100}
MOD={'muteUntil':None,'banUntil':None}
def mid(i):return str(uuid.UUID(int=0x10140000000040008000000000000000+i))
def row(i,text=None):return {'id':mid(i),'channelId':C,'authorId':U,'text':text or ('plain %d é😀'%i),'createdAt':T,'expiresAt':X}
def channel():return {'id':C,'epoch':E,'kind':'project','peerIds':[],'groupId':None,'revision':state['revision'],'expiresAt':X}
def reject(message):
 state['failure']=message;report_rejection(message)
def frame(data):
 payload=json.dumps(data,ensure_ascii=False,separators=(',',':')).encode()
 prefix=b'\x81'+(bytes([len(payload)])if len(payload)<126 else b'\x7e'+struct.pack('!H',len(payload)))
 with wire_lock:
  sock=state['socket']
  if sock:
   try:sock.sendall(prefix+payload)
   except OSError:pass
def event():
 state['event']+=1
 frame({'version':1,'type':'event','eventId':str(state['event']),'topic':'chat:'+C,'revision':state['revision'],'eventType':'changed'})
def resync(reason='SUBSCRIBED'):
 frame({'version':1,'type':'resync_required','eventId':'r'+str(state['event']),'topic':'chat:'+C,'reason':reason})
def exact(stream,count):
 value=b''
 while len(value)<count:
  part=stream.recv(count-len(value))
  if not part:raise EOFError()
  value+=part
 return value
class WS(socketserver.BaseRequestHandler):
 def handle(self):
  sock=self.request;sock.settimeout(20)
  try:
   request=b''
   while b'\r\n\r\n' not in request:request+=exact(sock,1)
   headers=dict(line.decode().split(': ',1)for line in request.split(b'\r\n')[1:]if b': 'in line)
   accept=base64.b64encode(hashlib.sha1((headers['Sec-WebSocket-Key']+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest()).decode()
   sock.sendall(('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: '+accept+'\r\n\r\n').encode())
   with lock:state['socket']=sock;state['connections']+=1;state['subscriptions'].clear()
   while not finished.is_set():
    first,second=exact(sock,2);size=second&127
    if size==126:size=struct.unpack('!H',exact(sock,2))[0]
    if size==127:size=struct.unpack('!Q',exact(sock,8))[0]
    if size>8192 or not(second&128):raise ValueError('invalid client frame')
    mask=exact(sock,4);payload=exact(sock,size);payload=bytes(x^mask[i%4]for i,x in enumerate(payload))
    if first&15==8:break
    if first&15!=1:continue
    data=json.loads(payload)
    if data['type']=='authenticate':
     if (data.get('apiKey'),data.get('sessionToken'),data.get('version'))!=('chat-key','chat-session',1):raise ValueError('wrong WS identity')
     frame({'version':1,'type':'ready','pollAfterSeconds':120})
    elif data['type']=='subscribe':
     topic=data.get('topic')
     with lock:
      allowed=state['allowed'] and topic=='chat:'+C
      if allowed:state['subscriptions'].add(topic)
     if allowed:
      frame({'version':1,'type':'subscribed','topic':'chat:'+C,'requestId':data['requestId']});resync()
     else:frame({'version':1,'type':'error','code':'SUBSCRIPTION_FORBIDDEN','topic':topic,'requestId':data['requestId']})
    elif data['type']=='unsubscribe':frame({'version':1,'type':'unsubscribed','topic':data['topic'],'requestId':data['requestId']})
    elif data['type']=='ping':frame({'version':1,'type':'pong'})
  except(EOFError,OSError):pass
  except Exception as error:reject('WS '+str(error))
  finally:
   with lock:
    if state['socket'] is sock:state['socket']=None;state['subscriptions'].clear()
class WSServer(socketserver.ThreadingTCPServer):allow_reuse_address=True;daemon_threads=True
class HTTP(BaseHTTPRequestHandler):
 def do_GET(self):self.handle_case('GET')
 def do_POST(self):self.handle_case('POST')
 def handle_case(self,method):
  try:self.case(method)
  except Exception as error:
   reject('HTTP '+str(error))
   try:send_json(self,422,{'message':str(error)})
   except OSError:pass
 def case(self,method):
  raw=self.rfile.read(int(self.headers.get('Content-Length',0)));body=json.loads(raw)if raw else None
  path=self.path
  if path.startswith('/fixture/'):
   action=path.split('/')[-1]
   with lock:
    if action=='status':return send_json(self,200,{'syncs':len(state['syncs']),'mutations':len(state['mutations']),'connections':state['connections'],'rows':len(state['rows']),'failure':state['failure']})
    if action=='bulk':
     state['rows']=[state['rows'][0]]+[row(i)for i in range(1,20)];state['revision']+=1;event()
    elif action in ['shift','bad']:
     state['rows']=state['rows'][1:]+[row(20 if action=='shift' else 21)];state['revision']+=1;state['bad']=action=='bad';event()
    elif action=='restore':state['bad']=False;resync()
    elif action=='burst':
     state['delay']=True
     for _ in range(100):event()
    elif action=='revoke':state['allowed']=False;resync('PERMISSIONS_CHANGED')
    elif action=='allow':state['allowed']=True
    elif action=='unsupported':state['unsupported']=True
    elif action=='fail-open':state['failOpen']=True
    elif action=='fail-report':state['failReport']=True
    elif action=='fail-auth':state['failAuth']=True
    elif action=='late-failed-sync':state['lateFailure']=True
    elif action=='late-clock-sync':state['lateClock']=True
    elif action=='short-message':
     short=row(200,'short-lived local expiry');short['expiresAt']='2026-10-16T12:01:00.000Z'
     state['rows']=(state['rows']+[short])[-20:];state['revision']+=1;event()
    elif action=='disconnect':
     sock=state['socket']
     if sock:sock.shutdown(socket.SHUT_RDWR);sock.close();state['socket']=None;state['subscriptions'].clear()
    elif action=='finish':finished.set()
    else:raise ValueError('unknown fixture action')
   return send_json(self,200,{'ok':True})
  if self.headers.get('X-API-Key')!='chat-key' or self.headers.get('Authorization')!='Bearer chat-session':raise ValueError('wrong HTTP identity')
  with lock:state['http']+=1
  if path=='/api/v1/app/realtime/session':
   if method!='POST' or body!={}:raise ValueError('wrong realtime handshake')
   return send_json(self,200,{'pollAfterSeconds':120})
  if method=='POST' and body and (body.get('text')=='late-denial' or body.get('reason')=='late-missing'):
   with lock:state['mutations'].append(('late',copy.deepcopy(body)))
   time.sleep(.4)
   return send_json(self,403 if body.get('text')=='late-denial' else 404,{'code':'CHAT_BANNED'if body.get('text')=='late-denial'else'CHANNEL_NOT_FOUND','message':'obsolete write refusal'})
  with lock:
   if state['unsupported'] and path.startswith(BASE):return send_json(self,501,{'code':'NOT_SUPPORTED','message':'Chat is Cloud only'})
   if state['failAuth'] and path.startswith(BASE):state['failAuth']=False;return send_json(self,401,{'code':'SESSION_REQUIRED','message':'session rejected'})
   if path==BASE+'/limits?userId='+U and method=='GET':return send_json(self,200,{'limits':LIMITS,'capabilities':{'chat':True,'realtime':True,'incrementalSync':True,'localHistory':True},'moderation':MOD,'serverTime':T})
   if path==BASE+'/channels/open' and method=='POST':
    state['mutations'].append(('open',copy.deepcopy(body)))
    opened=channel()
    if body=={'userId':U,'kind':'direct','peerId':'00000000-0000-4000-8000-000000002014'}:
     opened.update(id='00000000-0000-4000-8000-000000003015',kind='direct',peerIds=[U,body['peerId']])
    elif body=={'userId':U,'kind':'group','groupId':'00000000-0000-4000-8000-000000007014'}:
     opened.update(id='00000000-0000-4000-8000-000000003016',kind='group',groupId=body['groupId'])
    elif body!={'userId':U,'kind':'project'}:raise ValueError('wrong original open')
    if state['failOpen']:state['failOpen']=False;return send_json(self,503,{'message':'uncertain open'})
    return send_json(self,200,{'channel':opened,'serverTime':T})
   if path==BASE+'/channels/'+C+'/sync' and method=='POST':
    if not state['allowed'] or 'chat:'+C not in state['subscriptions']:raise ValueError('sync before current authorized subscription')
    if set(body)!={'userId','epoch','knownMessageIds'}or body['userId']!=U:raise ValueError('wrong sync body')
    known=body['knownMessageIds']if body['epoch']==E else[]
    if body['epoch']not in[None,E]or len(set(known))!=len(known)or len(known)>20:raise ValueError('bad incremental cache')
    # A known ID may have been removed since the previous snapshot; only current unknown rows are loaded.
    rows=copy.deepcopy(state['rows']);payload=[x for x in rows if x['id']not in known]
    if state['bad']:payload=[]
    value={'channel':channel(),'visibleMessageIds':[x['id']for x in rows],'messages':payload,'moderation':MOD,'serverTime':T}
    state['syncs'].append({'known':copy.deepcopy(known),'loaded':len(payload),'epoch':body['epoch']})
    delay=state['delay'];state['delay']=False
    obsolete=state['lateFailure'];state['lateFailure']=False
    clock=state['lateClock'];state['lateClock']=False
    if obsolete:delay=True
    if clock:value['serverTime']='2026-10-16T12:00:05.000Z';delay=True
   elif path==BASE+'/channels/'+C+'/messages' and method=='POST':
    state['mutations'].append(('send',copy.deepcopy(body)))
    if set(body)!={'userId','operationId','issuedAt','text'}or body['userId']!=U:raise ValueError('wrong original send')
    if body['text']=='muted':return send_json(self,403,{'code':'CHAT_MUTED','message':'send-only refusal'})
    if body['text']in['fault503','fault429','faultfeature']:
     code=503 if body['text']=='fault503' else 429
     return send_json(self,code,{'message':'expected fault',**({'code':'CHAT_RATE_LIMITED'}if body['text']=='faultfeature'else{})},{'Retry-After':'0'})
    operation=body['operationId'];previous=state['receipts'].get(operation)
    if previous:
     if previous['body']!=body:return send_json(self,409,{'code':'OPERATION_CONFLICT','message':'changed original'})
     return send_json(self,200,{'receipt':previous['receipt'],'replayed':True,'serverTime':T})
    original=row(0 if not state['receipts'] else 1000+len(state['receipts']));original['text']=body['text'];state['rows']=(state['rows']+[original])[-20:];state['revision']+=1
    if len({x['id']for x in state['rows']})!=len(state['rows']):raise ValueError('fixture duplicate message identity')
    receipt={'operationId':operation,'issuedAt':body['issuedAt'],'channelId':C,'messageId':original['id'],'createdAt':T,'expiresAt':X}
    state['receipts'][operation]={'receipt':receipt,'body':copy.deepcopy(body)}
    event()
    if body['text']=='lost-ack':return send_json(self,503,{'message':'committed but response lost'})
    return send_json(self,201,{'receipt':receipt,'replayed':False,'serverTime':T})
   elif path.startswith(BASE+'/operations/')and method=='GET':
    operation=path.split('/')[-1].split('?')[0]
    if path!=BASE+'/operations/'+operation+'?userId='+U:raise ValueError('wrong operation identity')
    receipt=state['receipts'].get(operation)
    if not receipt:return send_json(self,404,{'code':'OPERATION_NOT_FOUND','message':'unknown, never proof of no write'})
    value={'receipt':receipt['receipt'],'serverTime':T};delay=operation.endswith('9999')
   elif path.startswith(BASE+'/channels/'+C+'/messages/')and path.endswith('/report')and method=='POST':
    state['mutations'].append(('report',copy.deepcopy(body)))
    if set(body)!={'userId','reason'}or body['userId']!=U:raise ValueError('wrong original report')
    if body['reason']=='missing-message':return send_json(self,404,{'code':'MESSAGE_NOT_FOUND','message':'message removed, channel still readable'})
    if state['failReport']:state['failReport']=False;return send_json(self,503,{'message':'uncertain report'})
    message_id=path.split('/')[-2];previous=state['reports'].get(message_id)
    if previous and previous!=body['reason']:return send_json(self,409,{'code':'OPERATION_CONFLICT','message':'changed original reason'})
    replay=previous is not None;state['reports'][message_id]=body['reason']
    return send_json(self,200 if replay else 201,{'reportId':'00000000-0000-4000-8000-000000006014','status':'open','replayed':replay,'serverTime':T})
   else:raise ValueError('unexpected route '+method+' '+path)
  if delay:time.sleep(.4)
  if path.endswith('/sync') and obsolete:return send_json(self,403,{'code':'CHAT_BANNED','message':'obsolete sync refusal'})
  try:send_json(self,200,value)
  except OSError:pass
 def log_message(self,*args):pass
http=ThreadingHTTPServer(('127.0.0.1',int(os.environ.get('HORIZON_CHAT_TEST_PORT','20124'))),HTTP);http.daemon_threads=True
ws=WSServer(('127.0.0.1',int(os.environ.get('HORIZON_CHAT_WS_TEST_PORT','20125'))),WS)
threading.Thread(target=http.serve_forever,daemon=True).start();threading.Thread(target=ws.serve_forever,daemon=True).start()
if os.environ.get('MOCK_SERVER_READY_FILE'):
 with open(os.environ['MOCK_SERVER_READY_FILE'],'w')as ready:ready.write('ready')
finished.wait(180)
http.shutdown();ws.shutdown();http.server_close();ws.server_close()
if not finished.is_set():reject('fixture did not finish')
if state['failure']:raise SystemExit(1)
print('PASS: Chat actual HTTP+WS: %d HTTP, %d mutations, %d bounded syncs, %d sockets; no text frames/poll fallback'%(state['http'],len(state['mutations']),len(state['syncs']),state['connections']))
