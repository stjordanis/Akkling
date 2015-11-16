﻿//-----------------------------------------------------------------------
// <copyright file="ActorRefs.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
//     Copyright (C) 2015 Bartosz Sypytkowski <gttps://github.com/Horusiath>
// </copyright>
//-----------------------------------------------------------------------

[<AutoOpen>]
module Akkling.ActorRefs

open Akka.Actor
open Akka.Util
open System
open System.Threading.Tasks

/// <summary>
/// Typed version of <see cref="ICanTell"/> interface. Allows to tell/ask using only messages of restricted type.
/// </summary>
[<Interface>]
type ICanTell<'Message> = 
    inherit ICanTell
    abstract Tell : 'Message * IActorRef -> unit
    abstract Ask : 'Message * TimeSpan option -> Async<'Response>

/// <summary>
/// Typed version of <see cref="IActorRef"/> interface. Allows to tell/ask using only messages of restricted type.
/// </summary>
[<Interface>]
type IActorRef<'Message> = 
    inherit ICanTell<'Message>
    inherit IActorRef
    /// <summary>
    /// Changes the type of handled messages, returning new typed ActorRef.
    /// </summary>
    abstract Switch<'T> : unit -> IActorRef<'T>

/// <summary>
/// Wrapper around untyped instance of IActorRef interface.
/// </summary>
type TypedActorRef<'Message>(underlyingRef : IActorRef) as this = 
    
    /// <summary>
    /// Gets an underlying actor reference wrapped by current object.
    /// </summary>
    member __.Underlying = underlyingRef

    override __.ToString () = underlyingRef.ToString ()
    override __.GetHashCode () = underlyingRef.GetHashCode ()
    override __.Equals o = 
        match o with
        | :? IActorRef as ref -> (this :> IActorRef).Equals(ref)
        | _ -> false
    
    interface ICanTell with
        member __.Tell(message : obj, sender : IActorRef) = underlyingRef.Tell(message, sender)
    
    interface IActorRef<'Message> with
        
        /// <summary>
        /// Changes the type of handled messages, returning new typed ActorRef.
        /// </summary>
        member __.Switch<'T>() = TypedActorRef<'T>(underlyingRef) :> IActorRef<'T>
        
        member __.Tell(message : 'Message, sender : IActorRef) = underlyingRef.Tell(message :> obj, sender)
        member __.Ask(message : 'Message, timeout : TimeSpan option) : Async<'Response> = 
            underlyingRef
                .Ask(message, Option.toNullable timeout)
                .ContinueWith(Func<Task<obj>,'Response>(fun t -> t.Result :?> 'Response), TaskContinuationOptions.ExecuteSynchronously)
            |> Async.AwaitTask
        member __.Path = underlyingRef.Path
        
        member __.Equals other = 
            match other with
            | :? TypedActorRef<'Message> as typed -> underlyingRef.Equals(typed.Underlying)
            | _ -> underlyingRef.Equals other

        member __.CompareTo (other: obj) = 
            match other with
            | :? TypedActorRef<'Message> as typed -> underlyingRef.CompareTo(typed.Underlying)
            | _ -> underlyingRef.CompareTo(other)
        
        member __.CompareTo (other: IActorRef) =
            match other with
            | :? TypedActorRef<'Message> as typed -> underlyingRef.CompareTo(typed.Underlying)
            | _ -> underlyingRef.CompareTo(other)
    
    interface ISurrogated with
        member this.ToSurrogate system = 
            let surrogate : TypedActorRefSurrogate<'Message> = { Wrapped = underlyingRef.ToSurrogate system }
            surrogate :> ISurrogate

and TypedActorRefSurrogate<'Message> = 
    { Wrapped : ISurrogate }
    interface ISurrogate with
        member this.FromSurrogate system = 
            let tref = TypedActorRef<'Message>((this.Wrapped.FromSurrogate system) :?> IActorRef)
            tref :> ISurrogated

/// <summary>
/// Returns typed wrapper over provided actor reference.
/// </summary>
let inline typed (actorRef : IActorRef) : IActorRef<'Message> = 
    if actorRef :? TypedActorRef<'Message> then actorRef :?> TypedActorRef<'Message> :> IActorRef<'Message>
    else (TypedActorRef<'Message> actorRef) :> IActorRef<'Message>

/// <summary>
/// Returns untyped <see cref="IActorRef" /> form of current typed actor.
/// </summary>
/// <param name="typedRef"></param>
let inline untyped (typedRef: IActorRef<'Message>) : IActorRef =
    (typedRef :?> TypedActorRef<'Message>).Underlying
    
/// <summary>
/// Typed wrapper for <see cref="ActorSelection"/> objects.
/// </summary>
type TypedActorSelection<'Message>(selection : ActorSelection) = 

    /// <summary>
    /// Returns an underlying untyped <see cref="ActorSelection"/> instance.
    /// </summary>
    member __.Underlying = selection

    /// <summary>
    /// Gets and actor ref anchor for current selection.
    /// </summary>
    member __.Anchor with get (): IActorRef<'Message> = typed selection.Anchor

    /// <summary>
    /// Gets string representation for all elements in actor selection path.
    /// </summary>
    member __.PathString with get () = selection.PathString

    /// <summary>
    /// Gets collection of elements, actor selection path is build from.
    /// </summary>
    member __.Path with get () = selection.Path

    /// <summary>
    /// Sets collection of elements, actor selection path is build from.
    /// </summary>
    member __.Path with set (e) = selection.Path <- e

    override __.ToString () = selection.ToString ()

    /// <summary>
    /// Tries to resolve an actor reference from current actor selection.
    /// </summary>
    member __.ResolveOne (timeout: TimeSpan): Async<IActorRef<'Message>> = 
        let convertToTyped (t: System.Threading.Tasks.Task<IActorRef>) = typed t.Result
        selection.ResolveOne(timeout).ContinueWith(convertToTyped)
        |> Async.AwaitTask

    override x.Equals (o:obj) = 
        if obj.ReferenceEquals(x, o) then true
        else match o with
        | :? TypedActorSelection<'Message> as t -> x.Underlying.Equals t.Underlying
        | _ -> x.Underlying.Equals o

    override __.GetHashCode () = selection.GetHashCode() ^^^ typeof<'Message>.GetHashCode() 

    interface ICanTell with
        member __.Tell(message : obj, sender : IActorRef) = selection.Tell(message, sender)
    
    interface ICanTell<'Message> with
        member __.Tell(message : 'Message, sender : IActorRef) : unit = selection.Tell(message, sender)
        member __.Ask(message : 'Message, timeout : TimeSpan option) : Async<'Response> = 
            Async.AwaitTask(selection.Ask<'Response>(message, Option.toNullable timeout))
            
/// <summary>
/// Unidirectional send operator. 
/// Sends a message object directly to actor tracked by actorRef. 
/// </summary>
let inline (<!) (actorRef : #ICanTell<'Message>) (msg : 'Message) : unit = 
    actorRef.Tell(msg, ActorCell.GetCurrentSelfOrNoSender())

/// <summary> 
/// Bidirectional send operator. Sends a message object directly to actor 
/// tracked by actorRef and awaits for response send back from corresponding actor. 
/// </summary>
let inline (<?) (tell : #ICanTell<'Message>) (msg : 'Message) : Async<'Response> = tell.Ask<'Response>(msg, None)

/// Pipes an output of asynchronous expression directly to the recipients mailbox.
let pipeTo (sender : IActorRef) (recipient : ICanTell<'Message>) (computation : Async<'Message>): unit = 
    let success (result : 'Message) : unit = recipient.Tell(result, sender)
    let failure (err : exn) : unit = recipient.Tell(Status.Failure(err), sender)
    Async.StartWithContinuations(computation, success, failure, failure)

/// Pipe operator which sends an output of asynchronous expression directly to the recipients mailbox.
let inline (|!>) (computation : Async<'Message>) (recipient : ICanTell<'Message>) = 
    pipeTo ActorRefs.NoSender recipient computation

/// Pipe operator which sends an output of asynchronous expression directly to the recipients mailbox
let inline (<!|) (recipient : ICanTell<'Message>) (computation : Async<'Message>) = 
    pipeTo ActorRefs.NoSender recipient computation

/// <summary>
/// Returns an instance of <see cref="ActorSelection" /> for specified path. 
/// If no matching receiver will be found, a <see cref="ActorRefs.NoSender" /> instance will be returned. 
/// </summary>
let inline select (path : string) (selector : IActorRefFactory) : TypedActorSelection<'Message> = 
    TypedActorSelection(selector.ActorSelection path)
