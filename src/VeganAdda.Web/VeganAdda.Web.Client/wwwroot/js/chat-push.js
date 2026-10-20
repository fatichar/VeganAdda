var Le=()=>{};var He=function(e){let t=[],n=0;for(let r=0;r<e.length;r++){let i=e.charCodeAt(r);i<128?t[n++]=i:i<2048?(t[n++]=i>>6|192,t[n++]=i&63|128):(i&64512)===55296&&r+1<e.length&&(e.charCodeAt(r+1)&64512)===56320?(i=65536+((i&1023)<<10)+(e.charCodeAt(++r)&1023),t[n++]=i>>18|240,t[n++]=i>>12&63|128,t[n++]=i>>6&63|128,t[n++]=i&63|128):(t[n++]=i>>12|224,t[n++]=i>>6&63|128,t[n++]=i&63|128)}return t},rn=function(e){let t=[],n=0,r=0;for(;n<e.length;){let i=e[n++];if(i<128)t[r++]=String.fromCharCode(i);else if(i>191&&i<224){let o=e[n++];t[r++]=String.fromCharCode((i&31)<<6|o&63)}else if(i>239&&i<365){let o=e[n++],s=e[n++],c=e[n++],f=((i&7)<<18|(o&63)<<12|(s&63)<<6|c&63)-65536;t[r++]=String.fromCharCode(55296+(f>>10)),t[r++]=String.fromCharCode(56320+(f&1023))}else{let o=e[n++],s=e[n++];t[r++]=String.fromCharCode((i&15)<<12|(o&63)<<6|s&63)}}return t.join("")},je={byteToCharMap_:null,charToByteMap_:null,byteToCharMapWebSafe_:null,charToByteMapWebSafe_:null,ENCODED_VALS_BASE:"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789",get ENCODED_VALS(){return this.ENCODED_VALS_BASE+"+/="},get ENCODED_VALS_WEBSAFE(){return this.ENCODED_VALS_BASE+"-_."},HAS_NATIVE_SUPPORT:typeof atob=="function",encodeByteArray(e,t){if(!Array.isArray(e))throw Error("encodeByteArray takes an array as a parameter");this.init_();let n=t?this.byteToCharMapWebSafe_:this.byteToCharMap_,r=[];for(let i=0;i<e.length;i+=3){let o=e[i],s=i+1<e.length,c=s?e[i+1]:0,f=i+2<e.length,a=f?e[i+2]:0,V=o>>2,x=(o&3)<<4|c>>4,W=(c&15)<<2|a>>6,K=a&63;f||(K=64,s||(W=64)),r.push(n[V],n[x],n[W],n[K])}return r.join("")},encodeString(e,t){return this.HAS_NATIVE_SUPPORT&&!t?btoa(e):this.encodeByteArray(He(e),t)},decodeString(e,t){return this.HAS_NATIVE_SUPPORT&&!t?atob(e):rn(this.decodeStringToByteArray(e,t))},decodeStringToByteArray(e,t){this.init_();let n=t?this.charToByteMapWebSafe_:this.charToByteMap_,r=[];for(let i=0;i<e.length;){let o=n[e.charAt(i++)],c=i<e.length?n[e.charAt(i)]:0;++i;let a=i<e.length?n[e.charAt(i)]:64;++i;let x=i<e.length?n[e.charAt(i)]:64;if(++i,o==null||c==null||a==null||x==null)throw new te;let W=o<<2|c>>4;if(r.push(W),a!==64){let K=c<<4&240|a>>2;if(r.push(K),x!==64){let nn=a<<6&192|x;r.push(nn)}}}return r},init_(){if(!this.byteToCharMap_){this.byteToCharMap_={},this.charToByteMap_={},this.byteToCharMapWebSafe_={},this.charToByteMapWebSafe_={};for(let e=0;e<this.ENCODED_VALS.length;e++)this.byteToCharMap_[e]=this.ENCODED_VALS.charAt(e),this.charToByteMap_[this.byteToCharMap_[e]]=e,this.byteToCharMapWebSafe_[e]=this.ENCODED_VALS_WEBSAFE.charAt(e),this.charToByteMapWebSafe_[this.byteToCharMapWebSafe_[e]]=e,e>=this.ENCODED_VALS_BASE.length&&(this.charToByteMap_[this.ENCODED_VALS_WEBSAFE.charAt(e)]=e,this.charToByteMapWebSafe_[this.ENCODED_VALS.charAt(e)]=e)}}},te=class extends Error{constructor(){super(...arguments),this.name="DecodeBase64StringError"}},on=function(e){let t=He(e);return je.encodeByteArray(t,!0)},ne=function(e){return on(e).replace(/\./g,"")},Ue=function(e){try{return je.decodeString(e,!0)}catch(t){console.error("base64Decode failed: ",t)}return null};function sn(){if(typeof self<"u")return self;if(typeof window<"u")return window;if(typeof global<"u")return global;throw new Error("Unable to locate global object.")}var an=()=>sn().__FIREBASE_DEFAULTS__,cn=()=>{if(typeof process>"u"||typeof process.env>"u")return;let e=process.env.__FIREBASE_DEFAULTS__;if(e)return JSON.parse(e)},fn=()=>{if(typeof document>"u")return;let e;try{e=document.cookie.match(/__FIREBASE_DEFAULTS__=([^;]+)/)}catch{return}let t=e&&Ue(e[1]);return t&&JSON.parse(t)},un=()=>{try{return Le()||an()||cn()||fn()}catch(e){console.info(`Unable to get __FIREBASE_DEFAULTS__ due to: ${e}`);return}};var re=()=>un()?.config;var z=class{constructor(){this.reject=()=>{},this.resolve=()=>{},this.promise=new Promise((t,n)=>{this.resolve=t,this.reject=n})}wrapCallback(t){return(n,r)=>{n?this.reject(n):this.resolve(r),typeof t=="function"&&(this.promise.catch(()=>{}),t.length===1?t(n):t(n,r))}}};function q(){try{return typeof indexedDB=="object"}catch{return!1}}function G(){return new Promise((e,t)=>{try{let n=!0,r="validate-browser-context-for-indexeddb-analytics-module",i=self.indexedDB.open(r);i.onsuccess=()=>{i.result.close(),n||self.indexedDB.deleteDatabase(r),e(!0)},i.onupgradeneeded=()=>{n=!1},i.onerror=()=>{t(i.error?.message||"")}}catch(n){t(n)}})}function Ve(){return!(typeof navigator>"u"||!navigator.cookieEnabled)}var dn="FirebaseError",m=class e extends Error{constructor(t,n,r){super(n),this.code=t,this.customData=r,this.name=dn,Object.setPrototypeOf(this,e.prototype),Error.captureStackTrace&&Error.captureStackTrace(this,w.prototype.create)}},w=class{constructor(t,n,r){this.service=t,this.serviceName=n,this.errors=r}create(t,...n){let r=n[0]||{},i=`${this.service}/${t}`,o=this.errors[t],s=o?ln(o,r):"Error",c=`${this.serviceName}: ${s} (${i}).`;return new m(i,c,r)}};function ln(e,t){try{let n=0,r="";for(;n<e.length;){let i=e.indexOf("{$",n);if(i===-1){r+=e.substring(n);break}let o=e.indexOf("}",i+2);if(o===-1){r+=e.substring(n);break}let s=e.substring(i+2,o),c=t[s];r+=e.substring(n,i)+(c!=null?String(c):`<${s}?>`),n=o+1}return r}catch{return e}}function J(e,t){if(e===t)return!0;let n=Object.keys(e),r=Object.keys(t);for(let i of n){if(!r.includes(i))return!1;let o=e[i],s=t[i];if($e(o)&&$e(s)){if(!J(o,s))return!1}else if(o!==s)return!1}for(let i of r)if(!n.includes(i))return!1;return!0}function $e(e){return e!==null&&typeof e=="object"}var no=14400*1e3;function M(e){return e&&e._delegate?e._delegate:e}var l=class{constructor(t,n,r){this.name=t,this.instanceFactory=n,this.type=r,this.multipleInstances=!1,this.serviceProps={},this.instantiationMode="LAZY",this.onInstanceCreated=null}setInstantiationMode(t){return this.instantiationMode=t,this}setMultipleInstances(t){return this.multipleInstances=t,this}setServiceProps(t){return this.serviceProps=t,this}setInstanceCreatedCallback(t){return this.onInstanceCreated=t,this}};var S="[DEFAULT]";var ie=class{constructor(t,n){this.name=t,this.container=n,this.component=null,this.instances=new Map,this.instancesDeferred=new Map,this.instancesOptions=new Map,this.onInitCallbacks=new Map}get(t){let n=this.normalizeInstanceIdentifier(t);if(!this.instancesDeferred.has(n)){let r=new z;if(this.instancesDeferred.set(n,r),this.isInitialized(n)||this.shouldAutoInitialize())try{let i=this.getOrInitializeService({instanceIdentifier:n});i&&r.resolve(i)}catch{}}return this.instancesDeferred.get(n).promise}getImmediate(t){let n=this.normalizeInstanceIdentifier(t?.identifier),r=t?.optional??!1;if(this.isInitialized(n)||this.shouldAutoInitialize())try{return this.getOrInitializeService({instanceIdentifier:n})}catch(i){if(r)return null;throw i}else{if(r)return null;throw Error(`Service ${this.name} is not available`)}}getComponent(){return this.component}setComponent(t){if(t.name!==this.name)throw Error(`Mismatching Component ${t.name} for Provider ${this.name}.`);if(this.component)throw Error(`Component for ${this.name} has already been provided`);if(this.component=t,!!this.shouldAutoInitialize()){if(pn(t))try{this.getOrInitializeService({instanceIdentifier:S})}catch{}for(let[n,r]of this.instancesDeferred.entries()){let i=this.normalizeInstanceIdentifier(n);try{let o=this.getOrInitializeService({instanceIdentifier:i});r.resolve(o)}catch{}}}}clearInstance(t=S){this.instancesDeferred.delete(t),this.instancesOptions.delete(t),this.instances.delete(t)}async delete(){let t=Array.from(this.instances.values());await Promise.all([...t.filter(n=>"INTERNAL"in n).map(n=>n.INTERNAL.delete()),...t.filter(n=>"_delete"in n).map(n=>n._delete())])}isComponentSet(){return this.component!=null}isInitialized(t=S){return this.instances.has(t)}getOptions(t=S){return this.instancesOptions.get(t)||{}}initialize(t={}){let{options:n={}}=t,r=this.normalizeInstanceIdentifier(t.instanceIdentifier);if(this.isInitialized(r))throw Error(`${this.name}(${r}) has already been initialized`);if(!this.isComponentSet())throw Error(`Component ${this.name} has not been registered yet`);let i=this.getOrInitializeService({instanceIdentifier:r,options:n});for(let[o,s]of this.instancesDeferred.entries()){let c=this.normalizeInstanceIdentifier(o);r===c&&s.resolve(i)}return i}onInit(t,n){let r=this.normalizeInstanceIdentifier(n),i=this.onInitCallbacks.get(r)??new Set;i.add(t),this.onInitCallbacks.set(r,i);let o=this.instances.get(r);return o&&t(o,r),()=>{i.delete(t)}}invokeOnInitCallbacks(t,n){let r=this.onInitCallbacks.get(n);if(r)for(let i of r)try{i(t,n)}catch{}}getOrInitializeService({instanceIdentifier:t,options:n={}}){let r=this.instances.get(t);if(!r&&this.component&&(r=this.component.instanceFactory(this.container,{instanceIdentifier:hn(t),options:n}),this.instances.set(t,r),this.instancesOptions.set(t,n),this.invokeOnInitCallbacks(r,t),this.component.onInstanceCreated))try{this.component.onInstanceCreated(this.container,t,r)}catch{}return r||null}normalizeInstanceIdentifier(t=S){return this.component?this.component.multipleInstances?t:S:t}shouldAutoInitialize(){return!!this.component&&this.component.instantiationMode!=="EXPLICIT"}};function hn(e){return e===S?void 0:e}function pn(e){return e.instantiationMode==="EAGER"}var Y=class{constructor(t){this.name=t,this.providers=new Map}addComponent(t){let n=this.getProvider(t.name);if(n.isComponentSet())throw new Error(`Component ${t.name} has already been registered with ${this.name}`);n.setComponent(t)}addOrOverwriteComponent(t){this.getProvider(t.name).isComponentSet()&&this.providers.delete(t.name),this.addComponent(t)}getProvider(t){if(this.providers.has(t))return this.providers.get(t);let n=new ie(t,this);return this.providers.set(t,n),n}getProviders(){return Array.from(this.providers.values())}};var gn=[],d;(function(e){e[e.DEBUG=0]="DEBUG",e[e.VERBOSE=1]="VERBOSE",e[e.INFO=2]="INFO",e[e.WARN=3]="WARN",e[e.ERROR=4]="ERROR",e[e.SILENT=5]="SILENT"})(d||(d={}));var bn={debug:d.DEBUG,verbose:d.VERBOSE,info:d.INFO,warn:d.WARN,error:d.ERROR,silent:d.SILENT},mn=d.INFO,wn={[d.DEBUG]:"log",[d.VERBOSE]:"log",[d.INFO]:"info",[d.WARN]:"warn",[d.ERROR]:"error"},yn=(e,t,...n)=>{if(t<e.logLevel)return;let r=new Date().toISOString(),i=wn[t];if(i)console[i](`[${r}]  ${e.name}:`,...n);else throw new Error(`Attempted to log a message with an invalid logType (value: ${t})`)},X=class{constructor(t){this.name=t,this._logLevel=mn,this._logHandler=yn,this._userLogHandler=null,gn.push(this)}get logLevel(){return this._logLevel}set logLevel(t){if(!(t in d))throw new TypeError(`Invalid value "${t}" assigned to \`logLevel\``);this._logLevel=t}setLogLevel(t){this._logLevel=typeof t=="string"?bn[t]:t}get logHandler(){return this._logHandler}set logHandler(t){if(typeof t!="function")throw new TypeError("Value assigned to `logHandler` must be a function");this._logHandler=t}get userLogHandler(){return this._userLogHandler}set userLogHandler(t){this._userLogHandler=t}debug(...t){this._userLogHandler&&this._userLogHandler(this,d.DEBUG,...t),this._logHandler(this,d.DEBUG,...t)}log(...t){this._userLogHandler&&this._userLogHandler(this,d.VERBOSE,...t),this._logHandler(this,d.VERBOSE,...t)}info(...t){this._userLogHandler&&this._userLogHandler(this,d.INFO,...t),this._logHandler(this,d.INFO,...t)}warn(...t){this._userLogHandler&&this._userLogHandler(this,d.WARN,...t),this._logHandler(this,d.WARN,...t)}error(...t){this._userLogHandler&&this._userLogHandler(this,d.ERROR,...t),this._logHandler(this,d.ERROR,...t)}};var En=(e,t)=>t.some(n=>e instanceof n),We,Ke;function _n(){return We||(We=[IDBDatabase,IDBObjectStore,IDBIndex,IDBCursor,IDBTransaction])}function In(){return Ke||(Ke=[IDBCursor.prototype.advance,IDBCursor.prototype.continue,IDBCursor.prototype.continuePrimaryKey])}var ze=new WeakMap,se=new WeakMap,qe=new WeakMap,oe=new WeakMap,ce=new WeakMap;function Sn(e){let t=new Promise((n,r)=>{let i=()=>{e.removeEventListener("success",o),e.removeEventListener("error",s)},o=()=>{n(h(e.result)),i()},s=()=>{r(e.error),i()};e.addEventListener("success",o),e.addEventListener("error",s)});return t.then(n=>{n instanceof IDBCursor&&ze.set(n,e)}).catch(()=>{}),ce.set(t,e),t}function vn(e){if(se.has(e))return;let t=new Promise((n,r)=>{let i=()=>{e.removeEventListener("complete",o),e.removeEventListener("error",s),e.removeEventListener("abort",s)},o=()=>{n(),i()},s=()=>{r(e.error||new DOMException("AbortError","AbortError")),i()};e.addEventListener("complete",o),e.addEventListener("error",s),e.addEventListener("abort",s)});se.set(e,t)}var ae={get(e,t,n){if(e instanceof IDBTransaction){if(t==="done")return se.get(e);if(t==="objectStoreNames")return e.objectStoreNames||qe.get(e);if(t==="store")return n.objectStoreNames[1]?void 0:n.objectStore(n.objectStoreNames[0])}return h(e[t])},set(e,t,n){return e[t]=n,!0},has(e,t){return e instanceof IDBTransaction&&(t==="done"||t==="store")?!0:t in e}};function Ge(e){ae=e(ae)}function An(e){return e===IDBDatabase.prototype.transaction&&!("objectStoreNames"in IDBTransaction.prototype)?function(t,...n){let r=e.call(Q(this),t,...n);return qe.set(r,t.sort?t.sort():[t]),h(r)}:In().includes(e)?function(...t){return e.apply(Q(this),t),h(ze.get(this))}:function(...t){return h(e.apply(Q(this),t))}}function Tn(e){return typeof e=="function"?An(e):(e instanceof IDBTransaction&&vn(e),En(e,_n())?new Proxy(e,ae):e)}function h(e){if(e instanceof IDBRequest)return Sn(e);if(oe.has(e))return oe.get(e);let t=Tn(e);return t!==e&&(oe.set(e,t),ce.set(t,e)),t}var Q=e=>ce.get(e);function v(e,t,{blocked:n,upgrade:r,blocking:i,terminated:o}={}){let s=indexedDB.open(e,t),c=h(s);return r&&s.addEventListener("upgradeneeded",f=>{r(h(s.result),f.oldVersion,f.newVersion,h(s.transaction),f)}),n&&s.addEventListener("blocked",f=>n(f.oldVersion,f.newVersion,f)),c.then(f=>{o&&f.addEventListener("close",()=>o()),i&&f.addEventListener("versionchange",a=>i(a.oldVersion,a.newVersion,a))}).catch(()=>{}),c}function B(e,{blocked:t}={}){let n=indexedDB.deleteDatabase(e);return t&&n.addEventListener("blocked",r=>t(r.oldVersion,r)),h(n).then(()=>{})}var Dn=["get","getKey","getAll","getAllKeys","count"],Cn=["put","add","delete","clear"],fe=new Map;function Je(e,t){if(!(e instanceof IDBDatabase&&!(t in e)&&typeof t=="string"))return;if(fe.get(t))return fe.get(t);let n=t.replace(/FromIndex$/,""),r=t!==n,i=Cn.includes(n);if(!(n in(r?IDBIndex:IDBObjectStore).prototype)||!(i||Dn.includes(n)))return;let o=async function(s,...c){let f=this.transaction(s,i?"readwrite":"readonly"),a=f.store;return r&&(a=a.index(c.shift())),(await Promise.all([a[n](...c),i&&f.done]))[0]};return fe.set(t,o),o}Ge(e=>({...e,get:(t,n,r)=>Je(t,n)||e.get(t,n,r),has:(t,n)=>!!Je(t,n)||e.has(t,n)}));var de=class{constructor(t){this.container=t}getPlatformInfoString(){return this.container.getProviders().map(n=>{if(kn(n)){let r=n.getImmediate();return`${r.library}/${r.version}`}else return null}).filter(n=>n).join(" ")}};function kn(e){return e.getComponent()?.type==="VERSION"}var le="@firebase/app",Ye="0.16.2";var E=new X("@firebase/app"),On="@firebase/app-compat",Rn="@firebase/analytics-compat",Nn="@firebase/analytics",xn="@firebase/app-check-compat",Mn="@firebase/app-check",Bn="@firebase/auth",Pn="@firebase/auth-compat",Fn="@firebase/database",Ln="@firebase/data-connect",$n="@firebase/database-compat",Hn="@firebase/functions",jn="@firebase/functions-compat",Un="@firebase/installations",Vn="@firebase/installations-compat",Wn="@firebase/messaging",Kn="@firebase/messaging-compat",zn="@firebase/performance",qn="@firebase/performance-compat",Gn="@firebase/remote-config",Jn="@firebase/remote-config-compat",Yn="@firebase/storage",Xn="@firebase/storage-compat",Qn="@firebase/firestore",Zn="@firebase/ai",er="@firebase/firestore-compat",tr="firebase";var he="[DEFAULT]",nr={[le]:"fire-core",[On]:"fire-core-compat",[Nn]:"fire-analytics",[Rn]:"fire-analytics-compat",[Mn]:"fire-app-check",[xn]:"fire-app-check-compat",[Bn]:"fire-auth",[Pn]:"fire-auth-compat",[Fn]:"fire-rtdb",[Ln]:"fire-data-connect",[$n]:"fire-rtdb-compat",[Hn]:"fire-fn",[jn]:"fire-fn-compat",[Un]:"fire-iid",[Vn]:"fire-iid-compat",[Wn]:"fire-fcm",[Kn]:"fire-fcm-compat",[zn]:"fire-perf",[qn]:"fire-perf-compat",[Gn]:"fire-rc",[Jn]:"fire-rc-compat",[Yn]:"fire-gcs",[Xn]:"fire-gcs-compat",[Qn]:"fire-fst",[er]:"fire-fst-compat",[Zn]:"fire-vertex","fire-js":"fire-js",[tr]:"fire-js-all"};var P=new Map,rr=new Map,pe=new Map;function Xe(e,t){try{e.container.addComponent(t)}catch(n){E.debug(`Component ${t.name} failed to register with FirebaseApp ${e.name}`,n)}}function _(e){let t=e.name;if(pe.has(t))return E.debug(`There were multiple attempts to register component ${t}.`),!1;pe.set(t,e);for(let n of P.values())Xe(n,e);for(let n of rr.values())Xe(n,e);return!0}function L(e,t){let n=e.container.getProvider("heartbeat").getImmediate({optional:!0});return n&&n.triggerHeartbeat(),e.container.getProvider(t)}var ir={"no-app":"No Firebase App '{$appName}' has been created - call initializeApp() first","bad-app-name":"Illegal App name: '{$appName}'","duplicate-app":"Firebase App named '{$appName}' already exists with different {$mismatchedParam}. Existing: '{$oldValue}'. New: '{$newValue}'.","app-deleted":"Firebase App named '{$appName}' already deleted","server-app-deleted":"Firebase Server App has been deleted","no-options":"Need to provide options, when not being deployed to hosting via source.","invalid-app-argument":"firebase.{$appName}() takes either no argument or a Firebase App instance.","invalid-log-argument":"First argument to `onLog` must be null or a function.","idb-open":"Error thrown when opening IndexedDB. Original error: {$originalErrorMessage}.","idb-get":"Error thrown when reading from IndexedDB. Original error: {$originalErrorMessage}.","idb-set":"Error thrown when writing to IndexedDB. Original error: {$originalErrorMessage}.","idb-delete":"Error thrown when deleting from IndexedDB. Original error: {$originalErrorMessage}.","finalization-registry-not-supported":"FirebaseServerApp deleteOnDeref field defined but the JS runtime does not support FinalizationRegistry.","invalid-server-app-environment":"FirebaseServerApp is not for use in browser environments."},y=new w("app","Firebase",ir);var ge=class{constructor(t,n,r){this._isDeleted=!1,this._options={...t},this._config={...n},this._name=n.name,this._automaticDataCollectionEnabled=n.automaticDataCollectionEnabled,this._container=r,this.container.addComponent(new l("app",()=>this,"PUBLIC"))}get automaticDataCollectionEnabled(){return this.checkDestroyed(),this._automaticDataCollectionEnabled}set automaticDataCollectionEnabled(t){this.checkDestroyed(),this._automaticDataCollectionEnabled=t}get name(){return this.checkDestroyed(),this._name}get options(){return this.checkDestroyed(),this._options}get config(){return this.checkDestroyed(),this._config}get container(){return this._container}get isDeleted(){return this._isDeleted}set isDeleted(t){this._isDeleted=t}checkDestroyed(){if(this.isDeleted)throw y.create("app-deleted",{appName:this._name})}};function we(e,t={}){let n=e;typeof t!="object"&&(t={name:t});let r={name:he,automaticDataCollectionEnabled:!0,...t},i=r.name;if(typeof i!="string"||!i)throw y.create("bad-app-name",{appName:String(i)});if(n||(n=re()),!n)throw y.create("no-options");let o=P.get(i);if(o)if(J(n,o.options)){if(J(r,o.config))return o;throw y.create("duplicate-app",{appName:i,mismatchedParam:"config",oldValue:JSON.stringify(o.config),newValue:JSON.stringify(r)})}else throw y.create("duplicate-app",{appName:i,mismatchedParam:"options",oldValue:JSON.stringify(o.options),newValue:JSON.stringify(n)});let s=new Y(i);for(let f of pe.values())s.addComponent(f);let c=new ge(n,r,s);return P.set(i,c),c}function ye(e=he){let t=P.get(e);if(!t&&e===he&&re())return we();if(!t)throw y.create("no-app",{appName:e});return t}function tt(){return Array.from(P.values())}function p(e,t,n){let r=nr[e]??e;n&&(r+=`-${n}`);let i=r.match(/\s|\//),o=t.match(/\s|\//);if(i||o){let s=[`Unable to register library "${r}" with version "${t}":`];i&&s.push(`library name "${r}" contains illegal characters (whitespace or "/")`),i&&o&&s.push("and"),o&&s.push(`version name "${t}" contains illegal characters (whitespace or "/")`),E.warn(s.join(" "));return}_(new l(`${r}-version`,()=>({library:r,version:t}),"VERSION"))}var or="firebase-heartbeat-database",sr=1,F="firebase-heartbeat-store",ue=null;function nt(){return ue||(ue=v(or,sr,{upgrade:(e,t)=>{switch(t){case 0:try{e.createObjectStore(F)}catch(n){console.warn(n)}}}}).catch(e=>{throw y.create("idb-open",{originalErrorMessage:e.message})})),ue}async function ar(e){try{let n=(await nt()).transaction(F),r=await n.objectStore(F).get(rt(e));return await n.done,r}catch(t){if(t instanceof m)E.warn(t.message);else{let n=y.create("idb-get",{originalErrorMessage:t?.message});E.warn(n.message)}}}async function Qe(e,t){try{let r=(await nt()).transaction(F,"readwrite");await r.objectStore(F).put(t,rt(e)),await r.done}catch(n){if(n instanceof m)E.warn(n.message);else{let r=y.create("idb-set",{originalErrorMessage:n?.message});E.warn(r.message)}}}function rt(e){return`${e.name}!${e.options.appId}`}var cr=1024,fr=30,be=class{constructor(t){this.container=t,this._heartbeatsCache=null;let n=this.container.getProvider("app").getImmediate();this._storage=new me(n),this._heartbeatsCachePromise=this._storage.read().then(r=>(this._heartbeatsCache=r,r))}async triggerHeartbeat(){try{let n=this.container.getProvider("platform-logger").getImmediate().getPlatformInfoString(),r=Ze();if(this._heartbeatsCache?.heartbeats==null&&(this._heartbeatsCache=await this._heartbeatsCachePromise,this._heartbeatsCache?.heartbeats==null)||this._heartbeatsCache.lastSentHeartbeatDate===r||this._heartbeatsCache.heartbeats.some(i=>i.date===r))return;if(this._heartbeatsCache.heartbeats.push({date:r,agent:n}),this._heartbeatsCache.heartbeats.length>fr){let i=dr(this._heartbeatsCache.heartbeats);this._heartbeatsCache.heartbeats.splice(i,1)}return this._storage.overwrite(this._heartbeatsCache)}catch(t){E.warn(t)}}async getHeartbeatsHeader(){try{if(this._heartbeatsCache===null&&await this._heartbeatsCachePromise,this._heartbeatsCache?.heartbeats==null||this._heartbeatsCache.heartbeats.length===0)return"";let t=Ze(),{heartbeatsToSend:n,unsentEntries:r}=ur(this._heartbeatsCache.heartbeats),i=ne(JSON.stringify({version:2,heartbeats:n}));return this._heartbeatsCache.lastSentHeartbeatDate=t,r.length>0?(this._heartbeatsCache.heartbeats=r,await this._storage.overwrite(this._heartbeatsCache)):(this._heartbeatsCache.heartbeats=[],this._storage.overwrite(this._heartbeatsCache)),i}catch(t){return E.warn(t),""}}};function Ze(){return new Date().toISOString().substring(0,10)}function ur(e,t=cr){let n=[],r=e.slice();for(let i of e){let o=n.find(s=>s.agent===i.agent);if(o){if(o.dates.push(i.date),et(n)>t){o.dates.pop();break}}else if(n.push({agent:i.agent,dates:[i.date]}),et(n)>t){n.pop();break}r=r.slice(1)}return{heartbeatsToSend:n,unsentEntries:r}}var me=class{constructor(t){this.app=t,this._canUseIndexedDBPromise=this.runIndexedDBEnvironmentCheck()}async runIndexedDBEnvironmentCheck(){return q()?G().then(()=>!0).catch(()=>!1):!1}async read(){if(await this._canUseIndexedDBPromise){let n=await ar(this.app);return n?.heartbeats?n:{heartbeats:[]}}else return{heartbeats:[]}}async overwrite(t){if(await this._canUseIndexedDBPromise){let r=await this.read();return Qe(this.app,{lastSentHeartbeatDate:t.lastSentHeartbeatDate??r.lastSentHeartbeatDate,heartbeats:t.heartbeats})}else return}async add(t){if(await this._canUseIndexedDBPromise){let r=await this.read();return Qe(this.app,{lastSentHeartbeatDate:t.lastSentHeartbeatDate??r.lastSentHeartbeatDate,heartbeats:[...r.heartbeats,...t.heartbeats]})}else return}};function et(e){return ne(JSON.stringify({version:2,heartbeats:e})).length}function dr(e){if(e.length===0)return-1;let t=0,n=e[0].date;for(let r=1;r<e.length;r++)e[r].date<n&&(n=e[r].date,t=r);return t}function lr(e){_(new l("platform-logger",t=>new de(t),"PRIVATE")),_(new l("heartbeat",t=>new be(t),"PRIVATE")),p(le,Ye,e),p(le,Ye,"esm2020"),p("fire-js","")}lr("");var hr="firebase",pr="12.19.0";p(hr,pr,"app");var st="@firebase/installations",Se="0.6.24";var at=1e4,ct=`w:${Se}`,ft="FIS_v2",gr="https://firebaseinstallations.googleapis.com/v1",br=3600*1e3,mr="installations",wr="Installations";var yr={"missing-app-config-values":'Missing App configuration value: "{$valueName}"',"not-registered":"Firebase Installation is not registered.","installation-not-found":"Firebase Installation not found.","request-failed":'{$requestName} request failed with error "{$serverCode} {$serverStatus}: {$serverMessage}"',"app-offline":"Could not process request. Application offline.","delete-pending-registration":"Can't delete installation while there is a pending registration request."},T=new w(mr,wr,yr);function ut(e){return e instanceof m&&e.code.includes("request-failed")}function dt({projectId:e}){return`${gr}/projects/${e}/installations`}function lt(e){return{token:e.token,requestStatus:2,expiresIn:_r(e.expiresIn),creationTime:Date.now()}}async function ht(e,t){let r=(await t.json()).error;return T.create("request-failed",{requestName:e,serverCode:r.code,serverMessage:r.message,serverStatus:r.status})}function pt({apiKey:e}){return new Headers({"Content-Type":"application/json",Accept:"application/json","x-goog-api-key":e})}function Er(e,{refreshToken:t}){let n=pt(e);return n.append("Authorization",Ir(t)),n}async function gt(e){let t=await e();return t.status>=500&&t.status<600?e():t}function _r(e){return Number(e.replace("s","000"))}function Ir(e){return`${ft} ${e}`}async function Sr({appConfig:e,heartbeatServiceProvider:t},{fid:n}){let r=dt(e),i=pt(e),o=t.getImmediate({optional:!0});if(o){let a=await o.getHeartbeatsHeader();a&&i.append("x-firebase-client",a)}let s={fid:n,authVersion:ft,appId:e.appId,sdkVersion:ct},c={method:"POST",headers:i,body:JSON.stringify(s)},f=await gt(()=>fetch(r,c));if(f.ok){let a=await f.json();return{fid:a.fid||n,registrationStatus:2,refreshToken:a.refreshToken,authToken:lt(a.authToken)}}else throw await ht("Create Installation",f)}function bt(e){return new Promise(t=>{setTimeout(t,e)})}function vr(e){return btoa(String.fromCharCode(...e)).replace(/\+/g,"-").replace(/\//g,"_")}var Ar=/^[cdef][\w-]{21}$/,Ie="";function Tr(){try{let e=new Uint8Array(17);(self.crypto||self.msCrypto).getRandomValues(e),e[0]=112+e[0]%16;let n=Dr(e);return Ar.test(n)?n:Ie}catch{return Ie}}function Dr(e){return vr(e).substr(0,22)}function k(e){return`${e.appName}!${e.appId}`}var C=new Map;function mt(e,t){let n=k(e);wt(n,t),Or(n,t)}function Cr(e,t){yt();let n=k(e),r=C.get(n);r||(r=new Set,C.set(n,r)),r.add(t)}function kr(e,t){let n=k(e),r=C.get(n);r&&(r.delete(t),r.size===0&&C.delete(n),Et())}function wt(e,t){let n=C.get(e);if(n)for(let r of n)r(t)}function Or(e,t){let n=yt();n&&n.postMessage({key:e,fid:t}),Et()}var A=null;function yt(){return!A&&"BroadcastChannel"in self&&(A=new BroadcastChannel("[Firebase] FID Change"),A.onmessage=e=>{wt(e.data.key,e.data.fid)}),A}function Et(){C.size===0&&A&&(A.close(),A=null)}var Rr="firebase-installations-database",Nr=1,D="firebase-installations-store",Ee=null;function ve(){return Ee||(Ee=v(Rr,Nr,{upgrade:(e,t)=>{t===0&&e.createObjectStore(D)}})),Ee}async function Z(e,t){let n=k(e),i=(await ve()).transaction(D,"readwrite"),o=i.objectStore(D),s=await o.get(n);return await o.put(t,n),await i.done,(!s||s.fid!==t.fid)&&mt(e,t.fid),t}async function _t(e){let t=k(e),r=(await ve()).transaction(D,"readwrite");await r.objectStore(D).delete(t),await r.done}async function ee(e,t){let n=k(e),i=(await ve()).transaction(D,"readwrite"),o=i.objectStore(D),s=await o.get(n),c=t(s);return c===void 0?await o.delete(n):await o.put(c,n),await i.done,c&&(!s||s.fid!==c.fid)&&mt(e,c.fid),c}async function Ae(e){let t,n=await ee(e.appConfig,r=>{let i=xr(r),o=Mr(e,i);return t=o.registrationPromise,o.installationEntry});return n.fid===Ie?{installationEntry:await t}:{installationEntry:n,registrationPromise:t}}function xr(e){let t=e||{fid:Tr(),registrationStatus:0};return It(t)}function Mr(e,t){if(t.registrationStatus===0){if(!navigator.onLine){let i=Promise.reject(T.create("app-offline"));return{installationEntry:t,registrationPromise:i}}let n={fid:t.fid,registrationStatus:1,registrationTime:Date.now()},r=Br(e,n);return{installationEntry:n,registrationPromise:r}}else return t.registrationStatus===1?{installationEntry:t,registrationPromise:Pr(e)}:{installationEntry:t}}async function Br(e,t){try{let n=await Sr(e,t);return Z(e.appConfig,n)}catch(n){throw ut(n)&&n.customData.serverCode===409?await _t(e.appConfig):await Z(e.appConfig,{fid:t.fid,registrationStatus:0}),n}}async function Pr(e){let t=await it(e.appConfig);for(;t.registrationStatus===1;)await bt(100),t=await it(e.appConfig);if(t.registrationStatus===0){let{installationEntry:n,registrationPromise:r}=await Ae(e);return r||n}return t}function it(e){return ee(e,t=>{if(!t)throw T.create("installation-not-found");return It(t)})}function It(e){return Fr(e)?{fid:e.fid,registrationStatus:0}:e}function Fr(e){return e.registrationStatus===1&&e.registrationTime+at<Date.now()}async function Lr({appConfig:e,heartbeatServiceProvider:t},n){let r=$r(e,n),i=Er(e,n),o=t.getImmediate({optional:!0});if(o){let a=await o.getHeartbeatsHeader();a&&i.append("x-firebase-client",a)}let s={installation:{sdkVersion:ct,appId:e.appId}},c={method:"POST",headers:i,body:JSON.stringify(s)},f=await gt(()=>fetch(r,c));if(f.ok){let a=await f.json();return lt(a)}else throw await ht("Generate Auth Token",f)}function $r(e,{fid:t}){return`${dt(e)}/${t}/authTokens:generate`}async function Te(e,t=!1){let n,r=await ee(e.appConfig,o=>{if(!St(o))throw T.create("not-registered");let s=o.authToken;if(!t&&Ur(s))return o;if(s.requestStatus===1)return n=Hr(e,t),o;{if(!navigator.onLine)throw T.create("app-offline");let c=Wr(o);return n=jr(e,c),c}});return n?await n:r.authToken}async function Hr(e,t){let n=await ot(e.appConfig);for(;n.authToken.requestStatus===1;)await bt(100),n=await ot(e.appConfig);let r=n.authToken;return r.requestStatus===0?Te(e,t):r}function ot(e){return ee(e,t=>{if(!St(t))throw T.create("not-registered");let n=t.authToken;return Kr(n)?{...t,authToken:{requestStatus:0}}:t})}async function jr(e,t){try{let n=await Lr(e,t),r={...t,authToken:n};return await Z(e.appConfig,r),n}catch(n){if(ut(n)&&(n.customData.serverCode===401||n.customData.serverCode===404))await _t(e.appConfig);else{let r={...t,authToken:{requestStatus:0}};await Z(e.appConfig,r)}throw n}}function St(e){return e!==void 0&&e.registrationStatus===2}function Ur(e){return e.requestStatus===2&&!Vr(e)}function Vr(e){let t=Date.now();return t<e.creationTime||e.creationTime+e.expiresIn<t+br}function Wr(e){let t={requestStatus:1,requestTime:Date.now()};return{...e,authToken:t}}function Kr(e){return e.requestStatus===1&&e.requestTime+at<Date.now()}async function zr(e){let t=e,{installationEntry:n,registrationPromise:r}=await Ae(t);return r?r.catch(console.error):Te(t).catch(console.error),n.fid}async function qr(e,t=!1){let n=e;return await Gr(n),(await Te(n,t)).token}async function Gr(e){let{registrationPromise:t}=await Ae(e);t&&await t}function vt(e,t){let{appConfig:n}=e;return Cr(n,t),()=>{kr(n,t)}}function Jr(e){if(!e||!e.options)throw _e("App Configuration");if(!e.name)throw _e("App Name");let t=["projectId","apiKey","appId"];for(let n of t)if(!e.options[n])throw _e(n);return{appName:e.name,projectId:e.options.projectId,apiKey:e.options.apiKey,appId:e.options.appId}}function _e(e){return T.create("missing-app-config-values",{valueName:e})}var At="installations",Yr="installations-internal",Xr=e=>{let t=e.getProvider("app").getImmediate(),n=Jr(t),r=L(t,"heartbeat");return{app:t,appConfig:n,heartbeatServiceProvider:r,_delete:()=>Promise.resolve()}},Qr=e=>{let t=e.getProvider("app").getImmediate(),n=L(t,At).getImmediate();return{getId:()=>zr(n),getToken:i=>qr(n,i)}};function Zr(){_(new l(At,Xr,"PUBLIC")),_(new l(Yr,Qr,"PRIVATE"))}Zr();p(st,Se);p(st,Se,"esm2020");var ei="/firebase-messaging-sw.js",ti="/firebase-cloud-messaging-push-scope",Lt="BDOU99-h67HcA6JeFXHbSNMu7e2yNNu3RzoMj8TM4W88jITfq7ZmPvIM1Iv-4_l2LxQcYwhqby2xGpWwzjfAnG4",ni="https://fcmregistrations.googleapis.com/v1",$t="google.c.a.c_id",ri="google.c.a.c_l",ii="google.c.a.ts",oi="google.c.a.e",Tt=1e4,Dt;(function(e){e[e.DATA_MESSAGE=1]="DATA_MESSAGE",e[e.DISPLAY_NOTIFICATION=3]="DISPLAY_NOTIFICATION"})(Dt||(Dt={}));var O;(function(e){e.PUSH_RECEIVED="push-received",e.NOTIFICATION_CLICKED="notification-clicked",e.FID_REGISTERED="fid-registered"})(O||(O={}));function g(e){let t=new Uint8Array(e);return btoa(String.fromCharCode(...t)).replace(/=/g,"").replace(/\+/g,"-").replace(/\//g,"_")}function Ht(e){let t="=".repeat((4-e.length%4)%4),n=(e+t).replace(/\-/g,"+").replace(/_/g,"/"),r=atob(n),i=new Uint8Array(r.length);for(let o=0;o<r.length;++o)i[o]=r.charCodeAt(o);return i}var De="fcm_token_details_db",si=5,Ct="fcm_token_object_Store";async function ai(e){if("databases"in indexedDB&&!(await indexedDB.databases()).map(o=>o.name).includes(De))return null;let t=null;return(await v(De,si,{upgrade:async(r,i,o,s)=>{if(i<2||!r.objectStoreNames.contains(Ct))return;let c=s.objectStore(Ct),f=await c.index("fcmSenderId").get(e);if(await c.clear(),!!f){if(i===2){let a=f;if(!a.auth||!a.p256dh||!a.endpoint)return;t={token:a.fcmToken,createTime:a.createTime??Date.now(),subscriptionOptions:{auth:a.auth,p256dh:a.p256dh,endpoint:a.endpoint,swScope:a.swScope,vapidKey:typeof a.vapidKey=="string"?a.vapidKey:g(a.vapidKey)}}}else if(i===3){let a=f;t={token:a.fcmToken,createTime:a.createTime,subscriptionOptions:{auth:g(a.auth),p256dh:g(a.p256dh),endpoint:a.endpoint,swScope:a.swScope,vapidKey:g(a.vapidKey)}}}else if(i===4){let a=f;t={token:a.fcmToken,createTime:a.createTime,subscriptionOptions:{auth:g(a.auth),p256dh:g(a.p256dh),endpoint:a.endpoint,swScope:a.swScope,vapidKey:g(a.vapidKey)}}}}}})).close(),await B(De),await B("fcm_vapid_details_db"),await B("undefined"),ci(t)?t:null}function ci(e){if(!e||!e.subscriptionOptions)return!1;let{subscriptionOptions:t}=e;return typeof e.createTime=="number"&&e.createTime>0&&typeof e.token=="string"&&e.token.length>0&&typeof t.auth=="string"&&t.auth.length>0&&typeof t.p256dh=="string"&&t.p256dh.length>0&&typeof t.endpoint=="string"&&t.endpoint.length>0&&typeof t.swScope=="string"&&t.swScope.length>0&&typeof t.vapidKey=="string"&&t.vapidKey.length>0}var fi={"missing-app-config-values":'Missing App configuration value: "{$valueName}"',"only-available-in-window":"This method is available in a Window context.","only-available-in-sw":"This method is available in a service worker context.","permission-default":"The notification permission was not granted and dismissed instead.","permission-blocked":"The notification permission was not granted and blocked instead.","unsupported-browser":"This browser doesn't support the API's required to use the Firebase SDK.","indexed-db-unsupported":"This browser doesn't support indexedDb.open() (ex. Safari iFrame, Firefox Private Browsing, etc)","failed-service-worker-registration":"We are unable to register the default service worker. {$browserErrorMessage}","token-subscribe-failed":"A problem occurred while subscribing the user to FCM: {$errorInfo}","token-subscribe-no-token":"FCM returned no token when subscribing the user to push.","fid-registration-failed":"A problem occurred while creating an FCM registration via FID: {$errorInfo}","fid-unregister-failed":"A problem occurred while unregistering the FCM registration via FID: {$errorInfo}","fid-registration-idb-schema-unavailable":"Unable to read or persist FID registration metadata because the messaging IndexedDB schema is unavailable (for example, the database could not be upgraded to the latest version).","token-unsubscribe-failed":"A problem occurred while unsubscribing the user from FCM: {$errorInfo}","token-update-failed":"A problem occurred while updating the user from FCM: {$errorInfo}","token-update-no-token":"FCM returned no token when updating the user to push.","use-sw-after-get-token":"The useServiceWorker() method may only be called once and must be called before calling getToken() to ensure your service worker is used.","invalid-sw-registration":"The input to useServiceWorker() must be a ServiceWorkerRegistration.","invalid-bg-handler":"The input to setBackgroundMessageHandler() must be a function.","invalid-vapid-key":"The public VAPID key must be a string.","use-vapid-key-after-get-token":"The usePublicVapidKey() method may only be called once and must be called before calling getToken() to ensure your VAPID key is used.","invalid-on-registered-handler":"No onRegistered callback handler was provided or registered. Implement onRegistered() before register()."},u=new w("messaging","Messaging",fi);var kt="firebase-messaging-database",Ot=2,I="firebase-messaging-store",b="firebase-messaging-fid-registration-store",ui={openDB:v,deleteDB:B},Rt=ui,$=null;function di(e,t,n){switch(t){case 0:if(e.createObjectStore(I),n===1)break;case 1:n===2&&e.createObjectStore(b)}}function Nt(e){return{upgrade:(t,n)=>{di(t,n,e)},blocked:()=>{},blocking:(t,n,r)=>{$=null,r.target?.close()},terminated:()=>{$=null}}}function R(){return $||($=Rt.openDB(kt,Ot,Nt(2)).catch(()=>Rt.openDB(kt,Ot-1,Nt(1)))),$}function jt(e,t){return e.objectStoreNames.contains(t)}function Re(e){if(!jt(e,b))throw u.create("fid-registration-idb-schema-unavailable")}async function Ut(e){let t=N(e),r=await(await R()).transaction(I).objectStore(I).get(t);if(r)return r;{let i=await ai(e.appConfig.senderId);if(i)return await Ne(e,i),i}}async function Ne(e,t){let n=N(e),r=await R(),i=[I],o=jt(r,b);o&&i.push(b);let s=r.transaction(i,"readwrite");return await s.objectStore(I).put(t,n),o&&await s.objectStore(b).delete(n),await s.done,t}async function li(e){let t=N(e),r=(await R()).transaction(I,"readwrite");await r.objectStore(I).delete(t),await r.done}async function xe(e){let t=N(e),n=await R();return Re(n),await n.transaction(b).objectStore(b).get(t)}async function hi(e,t){let n=N(e),r=await R();Re(r);let i=r.transaction([I,b],"readwrite");return await i.objectStore(b).put(t,n),await i.objectStore(I).delete(n),await i.done,t}async function pi(e){let t=N(e),n=await R();Re(n);let r=n.transaction(b,"readwrite");await r.objectStore(b).delete(t),await r.done}function N({appConfig:e}){return e.appId}var xt="@firebase/messaging",ke="0.13.3";var gi=3,bi=1e3;async function mi(e,t){let n=await j(e),r=Me(t,e.appConfig.appName,!1),i={method:"POST",headers:n,body:JSON.stringify(r)},o;try{o=await(await fetch(H(e.appConfig),i)).json()}catch(s){throw u.create("token-subscribe-failed",{errorInfo:s?.toString()})}if(o.error){let s=o.error.message;throw u.create("token-subscribe-failed",{errorInfo:s})}if(!o.token)throw u.create("token-subscribe-no-token");return o.token}async function wi(e,t){let n=await j(e),r=Me(t,e.appConfig.appName,!0),i={method:"POST",headers:n,body:JSON.stringify(r)},o;try{o=await Si(()=>fetch(H(e.appConfig),i),gi,bi)}catch(f){throw u.create("fid-registration-failed",{errorInfo:f?.toString()})}if(o.ok)return{responseFid:await Ei(o)};let s;try{s=await o.json()}catch{throw u.create("fid-registration-failed",{errorInfo:o.statusText})}let c=s.error?.message??o.statusText;throw u.create("fid-registration-failed",{errorInfo:c})}async function yi(e,t){let r={method:"DELETE",headers:await j(e)},i;try{i=await fetch(`${H(e.appConfig)}/${t}`,r)}catch(o){throw u.create("fid-unregister-failed",{errorInfo:o?.toString()})}if(!i.ok)try{throw(await i.json()).error?.message??i.statusText}catch(o){throw u.create("fid-unregister-failed",{errorInfo:typeof o=="string"&&o||i.statusText||o?.toString()})}}async function Ei(e){let t=await e.text();if(!t.trim())throw u.create("fid-registration-failed",{errorInfo:"CreateRegistration succeeded but response body is empty"});let n;try{n=JSON.parse(t)}catch{throw u.create("fid-registration-failed",{errorInfo:"CreateRegistration succeeded but response body is not valid JSON"})}let r=n.name;if(typeof r!="string"||r.length===0)throw u.create("fid-registration-failed",{errorInfo:"CreateRegistration succeeded but response did not include a non-empty name"});return _i(r)}var Mt="/registrations/";function _i(e){let t=e.indexOf(Mt);if(t!==-1){let n=e.slice(t+Mt.length);if(n.length>0)return n}throw u.create("fid-registration-failed",{errorInfo:"CreateRegistration succeeded but response name is not a valid registration resource name"})}async function Ii(e,t){let n=await j(e),r=Me(t.subscriptionOptions,e.appConfig.appName,!1),i={method:"PATCH",headers:n,body:JSON.stringify(r)},o;try{o=await(await fetch(`${H(e.appConfig)}/${t.token}`,i)).json()}catch(s){throw u.create("token-update-failed",{errorInfo:s?.toString()})}if(o.error){let s=o.error.message;throw u.create("token-update-failed",{errorInfo:s})}if(!o.token)throw u.create("token-update-no-token");return o.token}async function Vt(e,t){let r={method:"DELETE",headers:await j(e)};try{let o=await(await fetch(`${H(e.appConfig)}/${t}`,r)).json();if(o.error){let s=o.error.message;throw u.create("token-unsubscribe-failed",{errorInfo:s})}}catch(i){throw u.create("token-unsubscribe-failed",{errorInfo:i?.toString()})}}async function Si(e,t,n){let r;for(let i=0;i<t;i++)try{return await e()}catch(o){if(r=o,i<t-1){let s=n*Math.pow(2,i);await new Promise(c=>setTimeout(c,s))}}throw r}function H({projectId:e}){return`${ni}/projects/${e}/registrations`}async function j({appConfig:e,installations:t}){let n=await t.getToken();return new Headers({"Content-Type":"application/json",Accept:"application/json","x-goog-api-key":e.apiKey,"x-goog-firebase-installations-auth":`FIS ${n}`})}function vi(e,t){try{if(/^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(e))return new URL(e).host}catch{}try{if(typeof self<"u"&&self.location?.href)return new URL(e,self.location.origin).host}catch{}return typeof self<"u"&&self.location?.host?self.location.host:t}function Me({p256dh:e,auth:t,endpoint:n,vapidKey:r,swScope:i},o,s){let c={web:{origin:vi(i,o),endpoint:n,auth:t,p256dh:e}};return s&&(c.fcm_sdk_version=ke),r!==Lt&&(c.web.applicationPubKey=r),c}var Ai=10080*60*1e3;async function Ti(e){let t=await Ri(e.swRegistration,e.vapidKey),n={vapidKey:e.vapidKey,swScope:e.swRegistration.scope,endpoint:t.endpoint,auth:g(t.getKey("auth")),p256dh:g(t.getKey("p256dh"))},r=await Ut(e.firebaseDependencies);if(r){if(Ni(r.subscriptionOptions,n))return Date.now()>=r.createTime+Ai?Oi(e,{token:r.token,createTime:Date.now(),subscriptionOptions:n}):r.token;try{await Vt(e.firebaseDependencies,r.token)}catch(i){console.warn(i)}return Bt(e.firebaseDependencies,n)}else return Bt(e.firebaseDependencies,n)}async function Di(e,t){await Vt(e.firebaseDependencies,t.token),await li(e.firebaseDependencies),await Wt(e.firebaseDependencies)}async function Ci(e){let n=(await xe(e.firebaseDependencies).catch(()=>{}))?.fid;n&&await yi(e.firebaseDependencies,n),await Wt(e.firebaseDependencies),n&&Mi(e,n)}async function ki(e){let t=await Ut(e.firebaseDependencies);t?await Di(e,t):await Ci(e);let n=await e.swRegistration.pushManager.getSubscription();return n?n.unsubscribe():!0}async function Oi(e,t){try{let n=await Ii(e.firebaseDependencies,t),r={...t,token:n,createTime:Date.now()};return await Ne(e.firebaseDependencies,r),n}catch(n){throw n}}async function Bt(e,t){let r={token:await mi(e,t),createTime:Date.now(),subscriptionOptions:t};return await Ne(e,r),r.token}async function Ri(e,t){let n=await e.pushManager.getSubscription();return n||e.pushManager.subscribe({userVisibleOnly:!0,applicationServerKey:Ht(t)})}function Ni(e,t){let n=t.vapidKey===e.vapidKey,r=t.endpoint===e.endpoint,i=t.auth===e.auth,o=t.p256dh===e.p256dh;return n&&r&&i&&o}async function Wt(e){try{await pi(e)}catch{}}function xi(e,t){let n=e.onRegisteredHandler;n&&(typeof n=="function"?n(t):n.next(t))}function Mi(e,t){let n=e.onUnregisteredHandler;n&&(typeof n=="function"?n(t):n.next(t))}async function Kt(e){try{e.swRegistration=await navigator.serviceWorker.register(ei,{scope:ti}),e.swRegistration.update().catch(()=>{}),await Bi(e.swRegistration)}catch(t){throw u.create("failed-service-worker-registration",{browserErrorMessage:t?.message})}}async function Bi(e){return new Promise((t,n)=>{let r=setTimeout(()=>n(new Error(`Service worker not registered after ${Tt} ms`)),Tt),i=e.installing||e.waiting;e.active?(clearTimeout(r),t()):i?i.onstatechange=o=>{o.target?.state==="activated"&&(i.onstatechange=null,clearTimeout(r),t())}:(clearTimeout(r),n(new Error("No incoming service worker found.")))})}async function zt(e,t){if(!t&&!e.swRegistration&&await Kt(e),!(!t&&e.swRegistration)){if(!(t instanceof ServiceWorkerRegistration))throw u.create("invalid-sw-registration");e.swRegistration=t}}async function qt(e,t){t?e.vapidKey=t:e.vapidKey||(e.vapidKey=Lt)}var Pt=3;async function Pi(e,t){let n=await Fi(e.swRegistration,e.vapidKey),r={vapidKey:e.vapidKey,swScope:e.swRegistration.scope,endpoint:n.endpoint,auth:g(n.getKey("auth")),p256dh:g(n.getKey("p256dh"))},i=e.firebaseDependencies.installations;for(let o=0;o<Pt;o++){let{responseFid:s}=await wi(e.firebaseDependencies,r);if(s===t)return;o<Pt-1&&await i.getToken(!0)}throw u.create("fid-registration-failed",{errorInfo:"CreateRegistration response FID does not match Firebase Installation ID"})}async function Fi(e,t){let n=await e.pushManager.getSubscription();return n||e.pushManager.subscribe({userVisibleOnly:!0,applicationServerKey:Ht(t)})}var Li=10080*60*1e3;async function Gt(e,t){if(!navigator)throw u.create("only-available-in-window");if(Notification.permission==="default"&&await Notification.requestPermission(),Notification.permission!=="granted")throw u.create("permission-blocked");if(!e.onRegisteredHandler)throw u.create("invalid-on-registered-handler");await qt(e,t?.vapidKey),await zt(e,t?.serviceWorkerRegistration);let n=e._registerNotifyChain.catch(()=>{});return e._registerNotifyChain=n.then(async()=>{let r=await e.firebaseDependencies.installations.getId(),i=await xe(e.firebaseDependencies),o=Date.now();if((!i||i.fid!==r||o>=i.lastRegisterTime+Li)&&(await Pi(e,r),await hi(e.firebaseDependencies,{fid:r,lastRegisterTime:o,vapidKey:e.vapidKey})),!e.onRegisteredHandler)throw u.create("invalid-on-registered-handler");xi(e,r)}),e._registerNotifyChain}function $i(e,t){return vt(t,()=>{(async()=>!e.onRegisteredHandler||!await xe(e.firebaseDependencies)||await Gt(e).catch(()=>{}))()})}function Ft(e){let t={from:e.from,collapseKey:e.collapse_key,messageId:e.fcmMessageId};return Hi(t,e),ji(t,e),Ui(t,e),t}function Hi(e,t){if(!t.notification)return;e.notification={};let n=t.notification.title;n&&(e.notification.title=n);let r=t.notification.body;r&&(e.notification.body=r);let i=t.notification.image;i&&(e.notification.image=i);let o=t.notification.icon;o&&(e.notification.icon=o)}function ji(e,t){t.data&&(e.data=t.data)}function Ui(e,t){if(!t.fcmOptions&&!t.notification?.click_action)return;e.fcmOptions={};let n=t.fcmOptions?.link??t.notification?.click_action;n&&(e.fcmOptions.link=n);let r=t.fcmOptions?.analytics_label;r&&(e.fcmOptions.analyticsLabel=r)}function Vi(e){return typeof e=="object"&&!!e&&$t in e}function Wi(e){if(!e||!e.options)throw Ce("App Configuration Object");if(!e.name)throw Ce("App Name");let t=["projectId","apiKey","appId","messagingSenderId"],{options:n}=e;for(let r of t)if(!n[r])throw Ce(r);return{appName:e.name,projectId:n.projectId,apiKey:n.apiKey,appId:n.appId,senderId:n.messagingSenderId}}function Ce(e){return u.create("missing-app-config-values",{valueName:e})}var Oe=class{constructor(t,n,r){this.deliveryMetricsExportedToBigQueryEnabled=!1,this.onBackgroundMessageHandler=null,this.onMessageHandler=null,this.onRegisteredHandler=null,this.onUnregisteredHandler=null,this._registerNotifyChain=Promise.resolve(),this._fidChangeUnsubscribe=null,this.logEvents=[],this.logQueue={state:"stopped"};let i=Wi(t);this.firebaseDependencies={app:t,appConfig:i,installations:n,analyticsProvider:r}}_delete(){return this._fidChangeUnsubscribe&&(this._fidChangeUnsubscribe(),this._fidChangeUnsubscribe=null),this.logQueue.state==="scheduled"&&clearTimeout(this.logQueue.timerId),this.logQueue={state:"stopped"},Promise.resolve()}};async function Jt(e,t){if(!navigator)throw u.create("only-available-in-window");if(Notification.permission==="default"&&await Notification.requestPermission(),Notification.permission!=="granted")throw u.create("permission-blocked");return await qt(e,t?.vapidKey),await zt(e,t?.serviceWorkerRegistration),Ti(e)}async function Ki(e,t,n){let r=zi(t);(await e.firebaseDependencies.analyticsProvider.get()).logEvent(r,{message_id:n[$t],message_name:n[ri],message_time:n[ii],message_device_time:Math.floor(Date.now()/1e3)})}function zi(e){switch(e){case O.NOTIFICATION_CLICKED:return"notification_open";case O.PUSH_RECEIVED:return"notification_foreground";default:throw new Error}}async function qi(e,t){let n=t.data;if(!n.isFirebaseMessaging)return;if(e.onMessageHandler&&n.messageType===O.PUSH_RECEIVED&&(typeof e.onMessageHandler=="function"?e.onMessageHandler(Ft(n)):e.onMessageHandler.next(Ft(n))),e.onRegisteredHandler&&n.messageType===O.FID_REGISTERED){let i=n.fid;typeof e.onRegisteredHandler=="function"?e.onRegisteredHandler(i):e.onRegisteredHandler.next(i)}let r=n.data;Vi(r)&&r[oi]==="1"&&await Ki(e,n.messageType,r)}var Gi=e=>{let t=new Oe(e.getProvider("app").getImmediate(),e.getProvider("installations-internal").getImmediate(),e.getProvider("analytics-internal"));return navigator.serviceWorker.addEventListener("message",n=>qi(t,n)),t._fidChangeUnsubscribe=$i(t,e.getProvider("installations").getImmediate()),t},Ji=e=>{let t=e.getProvider("messaging").getImmediate();return{getToken:r=>Jt(t,r),register:r=>Gt(t,r)}};function Yi(){_(new l("messaging",Gi,"PUBLIC")),_(new l("messaging-internal",Ji,"PRIVATE")),p(xt,ke),p(xt,ke,"esm2020")}async function Be(){try{await G()}catch{return!1}return typeof window<"u"&&q()&&Ve()&&"serviceWorker"in navigator&&"PushManager"in window&&"Notification"in window&&"fetch"in window&&ServiceWorkerRegistration.prototype.hasOwnProperty("showNotification")&&PushSubscription.prototype.hasOwnProperty("getKey")}async function Xi(e){if(!navigator)throw u.create("only-available-in-window");return e.swRegistration||await Kt(e),ki(e)}function Qi(e,t){if(!navigator)throw u.create("only-available-in-window");return e.onMessageHandler=t,()=>{e.onMessageHandler=null}}function Yt(e=ye()){return Be().then(t=>{if(!t)throw u.create("unsupported-browser")},t=>{throw u.create("indexed-db-unsupported")}),L(M(e),"messaging").getImmediate()}async function Xt(e,t){return e=M(e),Jt(e,t)}function Qt(e){return e=M(e),Xi(e)}function Zt(e,t){return e=M(e),Qi(e,t)}Yi();var U,Pe,en;function Ho(e){if(!e)return!1;let t="veganadda-chat-push-invite-seen";try{return localStorage.getItem(t)?!1:(localStorage.setItem(t,"true"),e)}catch{return!1}}async function Fe(e,t){let n=await(await fetch("/api/csrf")).json();if(!(await fetch("/api/push/device",{method:e,credentials:"same-origin",headers:{"Content-Type":"application/json","X-CSRF-TOKEN":n.token},body:t?JSON.stringify(t):void 0})).ok)throw new Error("Could not update notifications. Please try again.")}async function tn(){let e=await(await fetch("/api/push/config")).json();if(!e.enabled)throw new Error("Push notifications are not configured yet.");Pe=await navigator.serviceWorker.register("/firebase-messaging-sw.js"),await navigator.serviceWorker.ready;let t=tt().find(r=>r.name==="chat-push")||we(e.firebase,"chat-push");U=Yt(t),en||(en=Zt(U,async r=>{let i=await fetch("/api/push/device");if(!i.ok)return;let o=await i.json();!location.pathname.startsWith("/messages")&&o.enabled&&o.userId===r.data?.userId&&await Pe.showNotification("VeganAdda",{body:"You have a new chat message.",tag:"chat-"+r.data.id,data:{url:"/messages"}})}));let n=await Xt(U,{vapidKey:e.vapidKey,serviceWorkerRegistration:Pe});await Fe("POST",{token:n})}async function Zi(){if(!window.isSecureContext||!("Notification"in window)||!await Be())return"unsupported";if(!(await(await fetch("/api/push/config")).json()).enabled)return"unconfigured";if(Notification.permission==="denied")return await Fe("DELETE"),"blocked";let t=await fetch("/api/push/device");return t.ok&&(await t.json()).enabled&&Notification.permission==="granted"?"enabled":"off"}async function jo(){let e=await Zi();return e==="enabled"&&await tn(),e}async function Uo(){if(!("Notification"in window))throw new Error("Push notifications are unavailable in this browser.");return await Notification.requestPermission()!=="granted"?"blocked":(await tn(),"enabled")}async function Vo(){await Fe("DELETE"),U&&await Qt(U);let e=await navigator.serviceWorker.getRegistration("/");if(e)for(let t of await e.getNotifications())t.close();return"off"}export{Ho as claimInvite,Vo as disable,Uo as enable,jo as refresh,Zi as status};
/*! Bundled license information:

@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
@firebase/logger/dist/esm/index.esm.js:
@firebase/messaging/dist/esm/index.esm.js:
@firebase/messaging/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2017 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/util/dist/index.esm.js:
@firebase/util/dist/index.esm.js:
  (**
   * @license
   * Copyright 2022 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/util/dist/index.esm.js:
  (**
   * @license
   * Copyright 2017 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2021 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/util/dist/index.esm.js:
@firebase/component/dist/esm/index.esm.js:
@firebase/app/dist/esm/index.esm.js:
@firebase/app/dist/esm/index.esm.js:
@firebase/app/dist/esm/index.esm.js:
@firebase/installations/dist/esm/index.esm.js:
@firebase/installations/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2019 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/util/dist/index.esm.js:
firebase/app/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2020 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/util/dist/index.esm.js:
  (**
   * @license
   * Copyright 2021 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2025 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/util/dist/index.esm.js:
  (**
   * @license
   * Copyright 2025 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/app/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2019 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2023 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/app/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2021 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2019 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/installations/dist/esm/index.esm.js:
@firebase/installations/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2019 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2020 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/messaging/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2019 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2018 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except
   * in compliance with the License. You may obtain a copy of the License at
   *
   * http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software distributed under the License
   * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
   * or implied. See the License for the specific language governing permissions and limitations under
   * the License.
   *)
  (**
   * @license
   * Copyright 2017 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2020 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
  (**
   * @license
   * Copyright 2026 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)

@firebase/messaging/dist/esm/index.esm.js:
@firebase/messaging/dist/esm/index.esm.js:
  (**
   * @license
   * Copyright 2026 Google LLC
   *
   * Licensed under the Apache License, Version 2.0 (the "License");
   * you may not use this file except in compliance with the License.
   * You may obtain a copy of the License at
   *
   *   http://www.apache.org/licenses/LICENSE-2.0
   *
   * Unless required by applicable law or agreed to in writing, software
   * distributed under the License is distributed on an "AS IS" BASIS,
   * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   * See the License for the specific language governing permissions and
   * limitations under the License.
   *)
*/
