﻿(*--------------------------------------------------------------------------*\
**  FsCheck                                                                 **
**  Copyright (c) 2008-2010 Kurt Schelfthout. All rights reserved.          **
**  http://www.codeplex.com/fscheck                                         **
**                                                                          **
**  This software is released under the terms of the Revised BSD License.   **
**  See the file License.txt for the full text.                             **
\*--------------------------------------------------------------------------*)

#light

namespace FsCheck

//First some type we're going to make default generators for later

type NonNegativeInt = NonNegativeInt of int with
    member x.Get = match x with NonNegativeInt r -> r
    static member op_Explicit(NonNegativeInt i) = i

type PositiveInt = PositiveInt of int with
    member x.Get = match x with PositiveInt r -> r
    static member op_Explicit(PositiveInt i) = i

type NonZeroInt = NonZeroInt of int with
    member x.Get = match x with NonZeroInt r -> r
    static member op_Explicit(NonZeroInt i) = i

type NonEmptyString = NonEmptyString of string with
    member x.Get = match x with NonEmptyString r -> r
    override x.ToString() = x.Get

type StringNoNulls = StringNoNulls of string with
    member x.Get = match x with StringNoNulls r -> r
    override x.ToString() = x.Get

type Interval = Interval of int * int with
    member x.Left = match x with Interval (l,_) -> l
    member x.Right = match x with Interval (_,r) -> r

type IntWithMinMax = IntWithMinMax of int with
    member x.Get = match x with IntWithMinMax r -> r
    static member op_Explicit(IntWithMinMax i) = i

type NonEmptySet<'a when 'a : comparison> = NonEmptySet of Set<'a> with
    member x.Get = match x with NonEmptySet r -> r
    static member toSet(NonEmptySet s) = s
    
type NonEmptyArray<'a> = NonEmptyArray of 'a[] with
    member x.Get = match x with NonEmptyArray r -> r
    static member toArray(NonEmptyArray a) = a

type FixedLengthArray<'a> = FixedLengthArray of 'a[] with
    member x.Get = match x with FixedLengthArray r -> r
    static member toArray(FixedLengthArray a) = a

[<StructuredFormatDisplay("{StructuredDisplayAsTable}")>]
type Function<'a,'b when 'a : comparison> = F of ref<list<('a*'b)>> * ('a ->'b) with
    member x.Value = match x with F (_,f) -> f
    member x.Table = match x with F (table,_) -> !table
    member x.StructuredDisplayAsTable =
        x.ToString()
    override x.ToString() =
        let layoutTuple (x,y) = sprintf "%A->%A" x y
        x.Table 
        |> Seq.distinctBy fst 
        |> Seq.sortBy fst 
        |> Seq.map layoutTuple 
        |> String.concat "; "
        |> sprintf "{ %s }"
    static member from f = 
        let table = ref []
        F (table,fun x -> let y = f x in table := (x,y)::(!table); y)    

///Use the generator for 'a, but don't shrink.
type DontShrink<'a> = DontShrink of 'a



module Arb =

    open TypeClass
    open System

    let internal Arbitrary = ref <| TypeClass<Arbitrary<obj>>.New()

    ///Get the Arbitrary instance for the given type.
    let from<'Value> = (!Arbitrary).InstanceFor<'Value,Arbitrary<'Value>>()

    ///Returns a Gen<'Value>
    let generate<'Value> = from<'Value>.Generator

    ///Returns the immediate shrinks for the given value based on its type.
    let shrink<'Value> (a:'Value) = from<'Value>.Shrinker a

    let internal getGenerator t = (!Arbitrary).GetInstance t |> unbox<IArbitrary> |> (fun arb -> arb.GeneratorObj)

    let internal getShrink t = (!Arbitrary).GetInstance t |> unbox<IArbitrary> |> (fun arb -> arb.ShrinkerObj)

    ///Register the generators that are static members of the given type.
    let registerByType t = 
        let newTypeClass = (!Arbitrary).Discover(onlyPublic=true,instancesType=t)
        let result = (!Arbitrary).Compare newTypeClass
        Arbitrary := (!Arbitrary).Merge newTypeClass
        result

    ///Register the generators that are static members of the type argument.
    let register<'t>() = registerByType typeof<'t>

    /// Construct an Arbitrary instance from a generator.
    /// Shrink is not supported for this type.
    let fromGen (gen: Gen<'Value>) : Arbitrary<'Value> =
       { new Arbitrary<'Value>() with
           override x.Generator = gen
       }

    /// Construct an Arbitrary instance from a generator and shrinker.
    let fromGenShrink (gen: Gen<'Value>, shrinker: 'Value -> seq<'Value>): Arbitrary<'Value> =
       { new Arbitrary<'Value>() with
           override x.Generator = gen
           override x.Shrinker a = shrinker a
       }
      
    ///Construct an Arbitrary instance for a type that can be mapped to and from another type (e.g. a wrapper),
    ///based on a Arbitrary instance for the source type and two mapping functions. 
    let convert convertTo convertFrom (a:Arbitrary<'a>) =
        { new Arbitrary<'b>() with
           override x.Generator = a.Generator |> Gen.map convertTo
           override x.Shrinker b = b |> convertFrom |> a.Shrinker |> Seq.map convertTo
       }

    /// Return an Arbitrary instance that is a filtered version of an existing arbitrary instance.
    /// The generator uses Gen.suchThat, and the shrinks are filtered using Seq.filter with the given predicate.
    let filter pred (a:Arbitrary<'a>) =
        { new Arbitrary<'a>() with
           override x.Generator = a.Generator |> Gen.suchThat pred
           override x.Shrinker b = b |> a.Shrinker |> Seq.filter pred
       }

    /// Return an Arbitrary instance that is a mapped and filtered version of an existing arbitrary instance.
    /// The generator uses Gen.map with the given mapper and then Gen.suchThat with the given predicate, 
    /// and the shrinks are filtered using Seq.filter with the given predicate.
    ///This is sometimes useful if using just a filter would reduce the chance of getting a good value
    ///from the generator - and you can map the value instead. E.g. PositiveInt.
    let mapFilter mapper pred (a:Arbitrary<'a>) =
        { new Arbitrary<'a>() with
           override x.Generator = a.Generator |> Gen.map mapper |> Gen.suchThat pred
           override x.Shrinker b = b |> a.Shrinker |> Seq.filter pred
       }     
    
//TODO
//    /// Generate a subset of an existing set
//    let subsetOf (s: Set<'a>) : Gen<Set<'a>> =
//       gen { // Convert the set into an array
//             let setElems: 'a[] = Array.ofSeq s
//             // Generate indices into the array (up to the number of elements)
//             let! size = Gen.choose(0, s.Count)
//             let! indices = Gen.arrayOfSize (Gen.choose(0, s.Count-1)) size
//             // Extract the elements
//             let arr: 'a[] = indices |> Array.map (fun i -> setElems.[i])
//             // Construct a set (which eliminates dups)
//             return Set.ofArray arr }
//
//    /// Generate a non-empty subset of an existing (non-empty) set
//    let nonEmptySubsetOf (s: Set<'a>) : Gen<Set<'a>> =
//       gen { // Convert the set into an array
//             let setElems: 'a[] = Array.ofSeq s
//             // Generate indices into the array (up to the number of elements)
//             let! size = Gen.choose(1, s.Count)
//             let! indices = Gen.arrayOfLength (Gen.choose(0, s.Count-1)) size
//             // Extract the elements
//             let arr: 'a[] = indices |> Array.map (fun i -> setElems.[i])
//             // Construct a set (which eliminates dups)
//             return Set.ofArray arr }
  
    ///A collection of default generators.
    type Default =
        static member private fraction (a:int) (b:int) (c:int) = 
            double a + double b / (abs (double c) + 1.0) 
        ///Generates (), of the unit type.
        static member Unit() = 
            { new Arbitrary<unit>() with
                override x.Generator = gen { return () } 
            }
        ///Generates arbitrary bools.
        static member Bool() = 
            { new Arbitrary<bool>() with
                override x.Generator = Gen.elements [true; false] 
            }
        //byte generator contributed by Steve Gilham.
        ///Generates an arbitrary byte.
        static member Byte() =   
            { new Arbitrary<byte>() with  
                override x.Generator = 
                    Gen.choose (0,255) |> Gen.map byte //this is now size independent - 255 is not enough to not cover them all anyway 
                override x.Shrinker n = n |> int |> shrink |> Seq.map byte
            }  
        ///Generate arbitrary int that is between -size and size.
        static member Int() = 
            { new Arbitrary<int>() with
                override x.Generator = Gen.sized <| fun n -> Gen.choose (-n,n) 
                override x.Shrinker n = 
                    let (|>|) x y = abs x > abs y 
                    seq {   if n < 0 then yield -n
                            if n <> 0 then yield 0 
                            yield! Seq.unfold (fun st -> let st = st / 2 in Some (n-st, st)) n 
                                    |> Seq.takeWhile ((|>|) n) }
                    |> Seq.distinct
            }
        ///Generates arbitrary floats, NaN, NegativeInfinity, PositiveInfinity, Maxvalue, MinValue, Epsilon included fairly frequently.
        static member Float() = 
            { new Arbitrary<float>() with
                override x.Generator = 
                    Gen.frequency   [(6, Gen.map3 Default.fraction generate generate generate)
                                    ;(1, Gen.elements [ Double.NaN; Double.NegativeInfinity; Double.PositiveInfinity])
                                    ;(1, Gen.elements [ Double.MaxValue; Double.MinValue; Double.Epsilon])]
                override x.Shrinker fl =
                    let (|<|) x y = abs x < abs y
                    seq {   if Double.IsInfinity fl || Double.IsNaN fl then 
                                yield 0.0
                            else
                                if fl < 0.0 then yield -fl
                                let truncated = truncate fl
                                if truncated |<| fl then yield truncated }
                    |> Seq.distinct
            }
        ///Generates arbitrary chars, between ASCII codes Char.MinValue and 127.
        static member Char() = 
            { new Arbitrary<char>() with
                override x.Generator = Gen.choose (int Char.MinValue, 127) |> Gen.map char
                override x.Shrinker c =
                    seq { for c' in ['a';'b';'c'] do if c' < c || not (Char.IsLower c) then yield c' }
            }
        ///Generates arbitrary strings, which are lists of chars generated by Char.
        static member String() = 
            { new Arbitrary<string>() with
                override x.Generator = Gen.map (fun chars -> new String(List.toArray chars)) generate
                override x.Shrinker s = s.ToCharArray() |> Array.toList |> shrink |> Seq.map (fun chars -> new String(List.toArray chars))
            }
        ///Generate an option value that is 'None' 1/8 of the time.
        static member Option() = 
            { new Arbitrary<option<'a>>() with
                override x.Generator = Gen.frequency [(1, gen { return None }); (7, Gen.map Some generate)]
                override x.Shrinker o =
                    match o with
                    | Some x -> seq { yield None; for x' in shrink x -> Some x' }
                    | None  -> Seq.empty
            }
        ///Generate a list of values. The size of the list is between 0 and the test size + 1.
        static member FsList() = 
            { new Arbitrary<list<'a>>() with
                override x.Generator = Gen.listOf generate
                override x.Shrinker l =
                    match l with
                    | [] ->         Seq.empty
                    | (x::xs) ->    seq { yield xs
                                          for xs' in shrink xs -> x::xs'
                                          for x' in shrink x -> x'::xs }
            }
        ///Generate an object - a boxed char, string or boolean value.
        static member Object() =
            { new Arbitrary<obj>() with
                override x.Generator = 
                    Gen.oneof [ Gen.map box <| generate<char> ; Gen.map box <| generate<string>; Gen.map box <| generate<bool> ]
                override x.Shrinker o =
                    seq {
                        match o with
                        | :? char as c -> yield box true; yield box false; yield! shrink c |> Seq.map box
                        | :? string as s -> yield box true; yield box false; yield! shrink s |> Seq.map box
                        | :? bool as b -> yield! Seq.empty
                        | _ -> failwith "Unknown type in shrink of obj"
                    }
            }
        ///Generate a rank 1 array.
        static member Array() =
            { new Arbitrary<'a[]>() with
                override x.Generator = Gen.arrayOf generate
                override x.Shrinker a = a |> Array.toList |> shrink |> Seq.map List.toArray
            }

        ///Generate a rank 2, zero based array.
        static member Array2D() = 
            let shrinkArray2D (arr:_[,]) =
                let removeRow r (arr:_[,]) =
                    Array2D.init (Array2D.length1 arr-1) (Array2D.length2 arr) (fun i j -> if i < r then arr.[i,j] else arr.[i+1,j])
                let removeCol c (arr:_[,]) =
                    Array2D.init (Array2D.length1 arr) (Array2D.length2 arr-1) (fun i j -> if j < c then arr.[i,j] else arr.[i,j+1])
                seq { for r in 1..Array2D.length1 arr do yield removeRow r arr
                      for c in 1..Array2D.length2 arr do yield removeCol c arr
                      for i in 0..Array2D.length1 arr-1 do //hopefully the matrix is shrunk considerably before we get here...
                        for j in 0..Array2D.length2 arr-1 do
                            for elem in shrink arr.[i,j] do
                                let shrunk = Array2D.copy arr
                                shrunk.[i,j] <- elem
                                yield shrunk
                    }
                            
            fromGenShrink (Gen.array2DOf generate,shrinkArray2D)

         ///Generate a function value. Function values can be generated for types 'a->'b where 'a has a CoArbitrary
         ///value and 'b has an Arbitrary value.
        static member Arrow() = 
            { new Arbitrary<'a->'b>() with
                override x.Generator = Gen.promote (fun a -> Gen.variant a generate) //coarbitrary a arbitrary)
            }

        ///Generate a Function value that can be printed and shrunk. Function values can be generated for types 'a->'b where 'a has a CoArbitrary
         ///value and 'b has an Arbitrary value.
        static member Function() =
            { new Arbitrary<Function<'a,'b>>() with
                override x.Generator = Gen.map Function<'a,'b>.from generate
                override x.Shrinker f = 
                    let update x' y' f x = if x = x' then y' else f x
                    seq { for (x,y) in f.Table do 
                            for y' in shrink y do 
                                yield Function<'a,'b>.from (update x y' f.Value) }
            }

        ///Generates an arbitrary DateTime between 1900 and 2100. A DateTime is shrunk by removing its second, minute and hour compoonents.
        static member DateTime() = 
            let genDate = gen {  let! y = Gen.choose(1900,2100)
                                 let! m = Gen.choose(1, 12)
                                 let! d = Gen.choose(1, DateTime.DaysInMonth(y, m))
                                 let! h = Gen.choose(0,23)
                                 let! min = Gen.choose(0,59)
                                 let! sec = Gen.choose(0,59)
                                 return DateTime(y, m, d, h, min, sec) }
            let shrinkDate (d:DateTime) = 
                if d.Second <> 0 then
                    seq { yield DateTime(d.Year,d.Month,d.Day,d.Hour,d.Minute,0) }
                elif d.Minute <> 0 then
                    seq { yield DateTime(d.Year,d.Month,d.Day,d.Hour,0,0) }
                elif d.Second <> 0 then
                    seq { yield DateTime(d.Year,d.Month,d.Day) }
                else
                    Seq.empty
            fromGenShrink (genDate,shrinkDate)

        static member NonNegativeInt() =
           from<int> 
           |> mapFilter abs (fun i -> i >= 0)
           |> convert NonNegativeInt int

        static member PositiveInt() =
            from<int>
            |> mapFilter abs (fun i -> i > 0)
            |> convert PositiveInt int

        static member NonZeroInt() =
           from<int>
            |> filter ((<>) 0)
            |> convert NonZeroInt int

        static member IntWithMinMax() =
            { new Arbitrary<IntWithMinMax>() with
                override x.Generator = Gen.frequency    [ (1 ,Gen.elements [Int32.MaxValue; Int32.MinValue])
                                                          (10,generate) ] 
                                       |> Gen.map IntWithMinMax
                override x.Shrinker (IntWithMinMax i) = shrink i |> Seq.map IntWithMinMax }

        ///Generates an interval between two non-negative integers.
        static member Interval() =
            { new Arbitrary<Interval>() with
                override  x.Generator = 
                    gen { let! start,offset = Gen.two generate
                          return Interval (abs start,abs start+abs offset) } //TODO: shrinker
            }

        static member StringWithoutNullChars() =
            from<string>
            |> filter (not << String.exists ((=) '\000'))
            |> convert StringNoNulls string

        static member NonEmptyString() =
            from<string>
            |> filter (fun s -> s <> "" && not (String.exists ((=) '\000') s))
            |> convert NonEmptyString string

        static member Set() = 
            from<list<_>> 
            |> convert Set.ofList Set.toList

        static member Map() = 
            from<list<_>> 
            |> convert Map.ofList Map.toList

        static member NonEmptyArray() =
            from<_[]>
            |> filter (fun a -> Array.length a > 0)
            |> convert NonEmptyArray (fun (NonEmptyArray s) -> s)

        static member NonEmptySet() =
            from<Set<_>>
            |> filter (not << Set.isEmpty) 
            |> convert NonEmptySet (fun (NonEmptySet s) -> s)

        //Arrays whose length does not change when shrinking.
        static member FixedLengthArray() =
            { new Arbitrary<'a[]>() with
                override x.Generator = generate
                override x.Shrinker a = a |> Seq.mapi (fun i x -> shrink x |> Seq.map (fun x' ->
                                                           let data' = Array.copy a
                                                           data'.[i] <- x'
                                                           data')
                                                   ) |> Seq.concat
            }
            |> convert FixedLengthArray (fun (FixedLengthArray a) -> a)

        ///Overrides the shrinker of any type to be empty, i.e. not to shrink at all.
        static member DontShrink() =
            generate |> Gen.map DontShrink |> fromGen
            
        ///Try to derive an arbitrary instance for the given type reflectively. Works
        ///for record, union, tuple and enum types.
        static member Derive() =
            { new Arbitrary<'a>() with
                override x.Generator = ReflectArbitrary.reflectGen getGenerator
                override x.Shrinker a = ReflectArbitrary.reflectShrink getShrink a
            }
            
        //TODO: consider adding sbyte, float32, int16, int64, BigInteger, decimal, Generic.Collections types, TimeSpan, Map