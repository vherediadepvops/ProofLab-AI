/** Exploratory Type-A intervals. Never a total uncertainty or a hypothesis verdict. */
export interface PeriodInterval95 {
  estimateSeconds:number; standardUncertaintySeconds:number; marginSeconds:number;
  lowerSeconds:number; upperSeconds:number; relativeMarginPercent:number|null;
  degreesOfFreedom:number; cycles:number; confidenceLevel:0.95;
  scope:'REPEATABILITY_ONLY';
}
export interface PeriodComparison95 {
  prediction:PeriodInterval95; difference:PeriodInterval95;
  relativeDifferencePercent:number; tolerancePercent:number;
  globalPValue:null; globalDecision:'NOT_ASSESSED_MISSING_TOTAL_UNCERTAINTY';
}

function logGamma(z:number):number {
  const c=[676.5203681218851,-1259.1392167224028,771.32342877765313,
    -176.61502916214059,12.507343278686905,-.13857109526572012,
    9.9843695780195716e-6,1.5056327351493116e-7];
  z-=1;let x=.99999999999980993;
  for(let i=0;i<c.length;i++)x+=c[i]/(z+i+1);
  const t=z+7.5;return .5*Math.log(2*Math.PI)+(z+.5)*Math.log(t)-t+Math.log(x);
}
function betaFraction(a:number,b:number,x:number):number {
  const small=1e-300;let c=1,d=1-(a+b)*x/(a+1);
  if(Math.abs(d)<small)d=small;d=1/d;let h=d;
  for(let m=1;m<=250;m++) {
    let delta=0;
    for(const aa of [m*(b-m)*x/((a+2*m-1)*(a+2*m)),
      -(a+m)*(a+b+m)*x/((a+2*m)*(a+2*m+1))]) {
      d=1+aa*d;if(Math.abs(d)<small)d=small;
      c=1+aa/c;if(Math.abs(c)<small)c=small;
      d=1/d;delta=d*c;h*=delta;
    }
    if(Math.abs(delta-1)<3e-14)return h;
  }
  return h;
}
function betaRegularized(x:number,a:number,b:number):number {
  if(x<=0)return 0;if(x>=1)return 1;
  const front=Math.exp(logGamma(a+b)-logGamma(a)-logGamma(b)+a*Math.log(x)+b*Math.log1p(-x));
  return x<(a+1)/(a+b+2)?front*betaFraction(a,b,x)/a:
    1-front*betaFraction(b,a,1-x)/b;
}
/** t_(0.975, df), also valid for fractional Welch-Satterthwaite degrees of freedom. */
export function studentCritical95(df:number):number {
  if(!Number.isFinite(df)||df<1)return NaN;
  let low=0,high=16;
  for(let i=0;i<70;i++) {
    const t=(low+high)/2,tail=betaRegularized(df/(df+t*t),df/2,.5)/2;
    if(tail>.025)low=t;else high=t;
  }
  return (low+high)/2;
}
function interval(estimate:number,u:number,df:number,cycles:number):PeriodInterval95|null {
  const margin=studentCritical95(df)*u;
  if(!Number.isFinite(margin)||margin<=0)return null;
  return {estimateSeconds:estimate,standardUncertaintySeconds:u,marginSeconds:margin,
    lowerSeconds:estimate-margin,upperSeconds:estimate+margin,
    relativeMarginPercent:estimate===0?null:margin/Math.abs(estimate)*100,
    degreesOfFreedom:df,cycles,confidenceLevel:.95,scope:'REPEATABILITY_ONLY'};
}
export function repeatabilityInterval95(value:unknown):PeriodInterval95|null {
  if(!value||typeof value!=='object')return null;
  const a=value as Record<string,unknown>,t=a['periodSeconds'],u=a['statisticalStandardUncertaintySeconds'],n=a['usedCycles'];
  if(a['status']!=='CANDIDATE'||typeof t!=='number'||!Number.isFinite(t)||t<=0||
    typeof u!=='number'||!Number.isFinite(u)||u<=0||
    typeof n!=='number'||!Number.isInteger(n)||n<3||n>15000)return null;
  return interval(t,u,n-1,n);
}
export function compareRepeatability95(first:PeriodInterval95|null,second:PeriodInterval95|null,
  value:unknown):PeriodComparison95|null {
  if(!first||!second||!value||typeof value!=='object')return null;
  const p=value as Record<string,unknown>,l1=p['baselineLengthMeters'],l2=p['nextLengthMeters'],t=p['periodSeconds'],tol=p['tolerancePercent'];
  if([l1,l2,t,tol].some(v=>typeof v!=='number'||!Number.isFinite(v)||v<=0))return null;
  const ratio=Math.sqrt((l2 as number)/(l1 as number));
  if(Math.abs(first.estimateSeconds*ratio-(t as number))>1e-6)return null;
  // Lengths are treated as exact here ONLY for the explicitly partial repeatability calculation.
  const prediction=interval(t as number,first.standardUncertaintySeconds*ratio,first.degreesOfFreedom,first.cycles);
  if(!prediction)return null;
  const v1=prediction.standardUncertaintySeconds**2,v2=second.standardUncertaintySeconds**2;
  const df=(v1+v2)**2/(v1*v1/first.degreesOfFreedom+v2*v2/second.degreesOfFreedom);
  const difference=interval(second.estimateSeconds-(t as number),Math.sqrt(v1+v2),df,first.cycles+second.cycles);
  if(!prediction||!difference)return null;
  return {prediction,difference,relativeDifferencePercent:100*difference.estimateSeconds/(t as number),
    tolerancePercent:tol as number,globalPValue:null,globalDecision:'NOT_ASSESSED_MISSING_TOTAL_UNCERTAINTY'};
}
