using System.Text.Json;
using XdcConverter;
try {
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);int checks=0;
void Check(bool v,string message){if(!v)throw new Exception(message);checks++;}
var masks=new List<object>();
foreach(string name in new[]{"bayer","bayer4","blue"}){
 var mask=DitherMask.Get(name);var values=Enumerable.Range(0,mask.Size*mask.Size).Select(i=>mask.At(i%mask.Size,i/mask.Size)).ToArray();
 Check(values.Distinct().Count()==values.Length&&values.Min()>=0&&values.Max()<64,"Unique mask ranks");
 Check(Enumerable.Range(0,100).All(i=>mask.At(i,i)==mask.At(i+mask.Size,i+mask.Size)),"Mask tiles exactly");
 masks.Add(new{Name=name,Size=mask.Size,Ranks=values});
 foreach(bool late in new[]{false,true}){
  var job=new Job{Dither=name,CompositeNewCga=late};var writer=new FrameWriter(job);
  var gradient=new byte[96000];for(int i=0;i<32000;i++){gradient[i*3]=(byte)(i%160*255/159);gradient[i*3+1]=(byte)(i/160*255/199);gradient[i*3+2]=96;}
  var first=writer.Quantize(gradient);writer.Quantize(new byte[96000]);Check(first.SequenceEqual(writer.Quantize(gradient)),"Mask is static across frames/seeks");
  Check(first.All(v=>v<16),"CGA patterns valid");
  Check(writer.Quantize(new byte[96000]).All(v=>v==0),"Black endpoint");
  Check(writer.Quantize(Enumerable.Repeat((byte)255,96000).ToArray()).All(v=>v==15),"White endpoint");
  job.DitherStrength=0;var zero=new FrameWriter(job).Quantize(gradient);job.Dither="none";
  Check(zero.SequenceEqual(new FrameWriter(job).Quantize(gradient)),"Zero equals no dither");
 }
}
// Suppress low-frequency energy compared with independently shuffled white masks.
double LowEnergy(int[] ranks){double sum=0;foreach(int count in new[]{16,32,48})for(int ky=-1;ky<=1;ky++)for(int kx=-1;kx<=1;kx++)if(kx!=0||ky!=0){double re=0,im=0;for(int i=0;i<64;i++){double v=(ranks[i]<count?1:0)-count/64.0,a=2*Math.PI*(kx*(i%8)+ky*(i/8))/8;re+=v*Math.Cos(a);im+=v*Math.Sin(a);}sum+=re*re+im*im;}return sum;}
var blue=Enumerable.Range(0,64).Select(i=>DitherMask.Blue.At(i%8,i/8)).ToArray();double whiteEnergy=0;var random=new Random(1701);
for(int sample=0;sample<64;sample++){var ranks=Enumerable.Range(0,64).ToArray();random.Shuffle(ranks);whiteEnergy+=LowEnergy(ranks)/64;}
double blueEnergy=LowEnergy(blue);Check(blueEnergy<whiteEnergy*.5,"Blue noise suppresses low spatial frequencies");
var jobStable=new Job{TemporalStability=50};var quant=new FrameWriter(jobStable);
List<byte[]> RunSequence(out StabilizationStatistics stats){
 var engine=new NativeEncoder(1,200,30,150,735,75);var stable=new NtscStabilizer(jobStable);var packets=new List<byte[]>();
 void Frame(byte[] rgb){var pixels=quant.Quantize(rgb);stable.Apply(rgb,pixels,engine.Reconstructed);var packet=engine.Encode(pixels,stable.Benefits,stable.SceneCut);packets.Add(packet);Check(packet.Length<=engine.Statistics.VideoByteBudget&&engine.Statistics.MaximumCycles<=engine.Statistics.CycleBudget+1e-6,"Exact replay and byte budgets");}
 for(int f=0;f<15;f++)Frame(Enumerable.Repeat((byte)96,96000).ToArray());
 long before=stable.Statistics.HeldByteChanges;
 for(int f=0;f<12;f++)Frame(Enumerable.Repeat((byte)(f%2==0?95:96),96000).ToArray());
 Check(stable.Statistics.HeldByteChanges>before,"Small source fluctuations retain a coherent pattern");
 for(int f=0;f<10;f++)Frame(new byte[96000]);Check(engine.Reconstructed.ToArray().All(v=>v==0),"Dark cut converges without a ghost");
 var white=Enumerable.Repeat((byte)255,96000).ToArray();for(int f=0;f<10;f++)Frame(white);
 var expected=new byte[16384];NativeEncoder.PackPixels(quant.Quantize(white),1,200,expected);Check(engine.Reconstructed.SequenceEqual(expected),"Bright cut converges exactly");
 Check(stable.Statistics.SceneCuts>=2,"Large scene changes release history");
 var square=new byte[96000];for(int y=70;y<100;y++)for(int x=90;x<120;x++)for(int c=0;c<3;c++)square[(y*160+x)*3+c]=255;
 for(int f=0;f<10;f++)Frame(square);NativeEncoder.PackPixels(quant.Quantize(square),1,200,expected);Check(engine.Reconstructed.SequenceEqual(expected),"Edges clear without persistent trails");
 using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool caught=false;try{stable.Apply(square,quant.Quantize(square),engine.Reconstructed,cancelled.Token);}catch(OperationCanceledException){caught=true;}Check(caught,"Stabilizer cancellation");
 stats=stable.Statistics;return packets;
}
var firstRun=RunSequence(out var stats);var secondRun=RunSequence(out _);Check(firstRun.Count==secondRun.Count&&firstRun.Zip(secondRun).All(p=>p.First.SequenceEqual(p.Second)),"Fresh segment state and repeated jobs are deterministic");
foreach(var bad in new[]{new Job{TemporalStability=-1},new Job{TemporalStability=101},new Job{Dither="invalid"}}){bool caught=false;try{bad.ValidateColor();}catch(ArgumentException){caught=true;}Check(caught,"Invalid settings rejected");}
var result=new{Success=true,Checks=checks,BlueLowFrequencyEnergy=blueEnergy,WhiteLowFrequencyEnergy=whiteEnergy,Masks=masks,Stabilization=stats};File.WriteAllText(Path.Combine(output,"contracts.json"),JsonSerializer.Serialize(result,Data.Json));Console.WriteLine($"PASS: {checks} temporal/mask contracts; blue noise energy {blueEnergy:F3} vs white {whiteEnergy:F3}");

} catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
