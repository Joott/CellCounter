
namespace CounterFunctions

open FSharpAux
open FSharp.Stats
open System
open System.IO
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Threading
open System.Collections.Generic

module MarrWavelet =
    type MarrWavelet =  {
        Scale           : float    
        Zero            : float    
        Minimum         : float    
        Diameter        : float    
        Values          : float [,]
        PadAreaRadius   : int      
        LMdistance      : int      
        zFilterdist     : float    
                        }
    
    let marrWaveletCreator (radius : float) = 
        let functionMarr x (y:float) s = (1./(Math.PI*s**2.))*(1.-(x**2.+y**2.)/(2.*s**2.))*(Math.E**(-((x**2.+y**2.)/(2.*s**2.))))
        let functionValuesMarr scale list= Array.map (fun y -> (Array.map (fun x -> functionMarr x y scale) list)) list

        {
        Scale           = 0.7071 * (radius )
        Zero            = radius   
        Minimum         = radius*2.
        Diameter        = radius*2.
        Values          = Array2D.ofJaggedArray (functionValuesMarr (0.7071 * (radius)) [|-(ceil (3. * radius + 2.))..(ceil(3. * radius + 2.))|])
        PadAreaRadius   = ceil (3. * radius + 2.) |> int 
        LMdistance      = (1.2619 * (radius) + 1.3095) |> round 0 |> int 
        zFilterdist     = 3.
}

module Image =

    let loadTiff filePath=
        let stream = File.OpenRead(filePath)
        let tiffDecoder =
            new TiffBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat ||| BitmapCreateOptions.IgnoreImageCache,
                    BitmapCacheOption.None);   
        Seq.init (tiffDecoder.Frames.Count) (fun frameIndex ->
            let cFrame = tiffDecoder.Frames.[frameIndex]
            let bytesPerPixel = cFrame.Format.BitsPerPixel / 8
            let convertedBitmap = new FormatConvertedBitmap(cFrame, PixelFormats.Default, null, 0.) //new FormatConvertedBitmap(cFrame, PixelFormats.Gray16, null, 0.)
            let width  = convertedBitmap.PixelWidth
            let height = convertedBitmap.PixelHeight
            let stride = width * bytesPerPixel
            let bytes : byte[] = Array.zeroCreate (width * height * bytesPerPixel)
            convertedBitmap.CopyPixels(bytes, width * bytesPerPixel, 0)
            let pixelSize = bytesPerPixel
            Array2D.init width height (fun x y -> 
                BitConverter.ToInt16 (bytes,stride * y + x * pixelSize) //ToInt16 default
                )
        )
        |> Seq.head

    let paddTiff (data: 'a[,])=
        let padArray2DWithRandom (rnd:System.Random) (offset:int) (arr:'a[,]) =
            let rowCount = Array2D.length1 arr 
            let colCount = Array2D.length2 arr
            let rowPadding = rowCount + offset
            let colPadding = colCount + offset
            Array2D.init (rowCount + offset * 2) (colCount + offset * 2)
                (fun rowI colI -> 
                    if (rowI < offset || colI < offset) || (rowI >= rowPadding  || colI >= colPadding) then
                        arr.[rnd.Next(0,rowCount),rnd.Next(0,colCount)] 
                    else
                        arr.[rowI-offset,colI-offset]
                )
        let paddedRawData =
            let rnd = System.Random()
            data
            |> padArray2DWithRandom rnd 40
        paddedRawData

module Maxima =

    let inline C3DWT (marr: MarrWavelet.MarrWavelet) (frame:'a[,]) =   
        printfn "frame:\t%i\t%i" (Array2D.length1 frame) (Array2D.length2 frame)
        let resolutionPixelfst = (Array2D.length1 frame) - (40 * 2)
        let resolutionPixelsnd = (Array2D.length2 frame) - (40 * 2)
        let offset = marr.PadAreaRadius
        let paddingoffset = 40
        let (CWTArray2D0: float[,]) = Array2D.zeroCreate (Array2D.length2 frame) (Array2D.length1 frame)
        printfn "ctwarr:\t%i\t%i" (Array2D.length1 CWTArray2D0) (Array2D.length2 CWTArray2D0)
        for x = paddingoffset to (paddingoffset + (resolutionPixelsnd-1)) do
            printfn "x: %i" x
            for y = paddingoffset to (paddingoffset + (resolutionPixelfst-1)) do
                printfn "y: %i" y
                CWTArray2D0.[x,y] <-
                    let mutable acc = 0.                                       
                    for a = 0 to 2*offset do
                        if x > 1105 then printfn "checkp a:%i" a
                        for b = 0 to 2*offset do               
                            if x > 1105 then printfn "checkp 2 b:%i" b
                            acc <- acc + ((marr.Values).[a,b] * (frame.[(y+(a-offset)),(x+(b-offset))] |> float))
                    acc
        let deletePaddingArea =
            let arrayWithoutPaddingoffset = Array2D.zeroCreate ((Array2D.length1 CWTArray2D0)-(2*paddingoffset)) ((Array2D.length2 CWTArray2D0)-(2*paddingoffset))
            for i=paddingoffset to (Array2D.length1 CWTArray2D0)-(paddingoffset+1) do
                for j=paddingoffset to (Array2D.length2 CWTArray2D0)-(paddingoffset+1) do
                    arrayWithoutPaddingoffset.[(i-paddingoffset),(j-paddingoffset)] <- CWTArray2D0.[i,j]
            arrayWithoutPaddingoffset
        deletePaddingArea


    ///gets Marr, a framenumber, an offset and the number of pixels to look at in the surrounding, and gives [,] of localMaxima
    let inline findLocalMaxima dist frame =   
        ///gets single 2D Array with only Maxima in it and gives coordinates of local maxima
        let allmaximaArray (arr:float[,]) =
            let rec loop acc i j =
                if i < (Array2D.length1 arr)-1 then
                    if j < (Array2D.length2 arr)-1  then 
                        if (arr.[i,j]) > 0. then loop ((float i, float j)::acc) i (j+1) 
                        else loop acc i (j+1)    
                    else loop acc (i+1) 0
                else acc
            loop [] 0 0 
        let numberofsurpix = dist
        let (cWTPercArray: float [,]) = frame  
        let arrayOfMaxima = Array2D.zeroCreate ((Array2D.length1 cWTPercArray)) ((Array2D.length2 cWTPercArray))
        let checkListsForContinuousDecline b c numberofsurpix =      
            let createSurroundingPixelLists b c numberofsurpix =
                let rec loop i accN accS accW accE accNW accSW accNE accSE =
                    let imod = (i |> float) * 0.7071 |> floor |> int
                    if i <= numberofsurpix then 
                        loop (i+1)  (cWTPercArray.[b+i   ,c     ]::accN )
                                    (cWTPercArray.[b-i   ,c     ]::accS )
                                    (cWTPercArray.[b     ,c-i   ]::accW )
                                    (cWTPercArray.[b     ,c+i   ]::accE )
                                    (cWTPercArray.[b+imod,c-imod]::accNW)
                                    (cWTPercArray.[b-imod,c-imod]::accSW)
                                    (cWTPercArray.[b+imod,c+imod]::accNE)
                                    (cWTPercArray.[b-imod,c+imod]::accSE)
                    else [accN;accS;accW;accE;accNW;accSW;accNE;accSE]
                loop 0 [] [] [] [] [] [] [] [] 
    
            let surroundingList = createSurroundingPixelLists b c numberofsurpix

            let rec isSortedAsc (list: float list) = 
                match list with
                    | [] -> true
                    | [x] -> true
                    | x::((y::_)as t) -> if x > y then false else isSortedAsc(t) 
            let boolList = surroundingList |> List.map  (fun x -> isSortedAsc x)
            (boolList |> List.contains false) = false

        //calculates checkListsForContinuousDecline for every pixel
        for i=numberofsurpix to (Array2D.length1 cWTPercArray)-(numberofsurpix+1) do 
            for j=numberofsurpix to (Array2D.length2 cWTPercArray)-(numberofsurpix+1) do 
                if cWTPercArray.[i,j] >= 10. then                              
                    if checkListsForContinuousDecline i j numberofsurpix = true     
                        then arrayOfMaxima.[i,j] <- cWTPercArray.[i,j]              
                    else arrayOfMaxima.[i,j] <- 0.                                  
                else arrayOfMaxima.[i,j] <- 0.                                   
        allmaximaArray arrayOfMaxima

module Filter =

    let circleCutter (wvPicture: float[,]) (pointA: float * float) (pointB: float * float) =
        let centerXY        = ((fst pointA + fst pointB)/2.,(snd pointA + snd pointB)/2.)
        let radius          = (sqrt((fst pointA - fst pointB)**2. + (snd pointA - snd pointB)**2.))/2.
        let jaggedPicture   = wvPicture |> Array2D.toJaggedArray
        let cutPicture =    jaggedPicture
                            |> Array.mapi (fun y -> Array.mapi (fun x value->
                                let distanceCenter = sqrt ((float x - fst centerXY)**2. + (float y - snd centerXY)**2.)
                                if distanceCenter > radius then 0.
                                else value))
        cutPicture

    let circleCutterAdaptive (wvPicture: float[,]) (horizontalLeft: float * float) (horizontalRight: float * float) (verticalTop: float * float) (verticalBottom: float * float)=
        let centerXY         = (fst horizontalLeft + ((fst horizontalRight - fst horizontalLeft)/2.)),(snd verticalBottom + ((snd verticalTop - snd verticalBottom)/2.))
        let radiusHorizontal = (fst horizontalRight - fst horizontalLeft)/2.
        let radiusVertical   = (snd verticalTop - snd verticalBottom)/2.
        let jaggedPicture    = wvPicture |> Array2D.toJaggedArray
        let cutPicture =    jaggedPicture
                            |> Array.mapi (fun y -> Array.mapi (fun x value->
                                let distanceHorizontal = abs (float x - fst centerXY)
                                let distanceVertical = abs (float y - snd centerXY)
                                let distanceCenter = sqrt ((float x - fst centerXY)**2. + (float y - snd centerXY)**2.)
                                let adaptedRadius = (distanceHorizontal * radiusHorizontal + distanceVertical * radiusVertical) / (distanceHorizontal + distanceVertical)
                                    //if distanceHorizontal > distanceVertical then
                                    //    let multiplier = distanceVertical / distanceHorizontal
                                    //    let radius     = multiplier * radiusHorizontal + (1. - multiplier) * radiusVertical
                                    //    radius
                                    //else
                                    //    let multiplier = distanceHorizontal / distanceVertical
                                    //    let radius     = multiplier * radiusVertical + (1. - multiplier) * radiusHorizontal
                                    //    radius
                                if distanceCenter > adaptedRadius then 
                                    100000000.
                                else 
                                    value))
        cutPicture

    let threshold (transf: float[][]) (percentileValue: float) =
        let percentile = transf |> Array.concat |> Array.sort
        let cutOffValue = percentile.[int (((float percentile.Length) - 1.) * percentileValue)]
        transf
        |> JaggedArray.map (fun x -> if x < cutOffValue then 0. else x)