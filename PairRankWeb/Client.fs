namespace PairRankWeb

open WebSharper
open WebSharper.JavaScript
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Templating
open WebSharper.Mvu
open WebSharper.Sitelets
open WebSharper.InterfaceGenerator.CodeModel

[<JavaScript>]
module Client =
  let rec tails a =
      match a with
      | [] -> [[]]
      | (v::vs) -> (v::vs) :: tails vs

  let createPermutations items =
      let rec allPairs vs =
          match vs with
          | [] -> []
          | [_] -> []
          | v1::v2::vl -> (v1, v2) :: allPairs (v1 :: vl)
      let concatPairs acc values = acc @ (allPairs values) 
      items
      |> tails
      |> List.fold concatPairs []

  type Templates = Template<"wwwroot/index.html", clientLoad = ClientLoad.FromDocument>

  type EndPoint = 
    | [<EndPoint "/">] Home
    | [<EndPoint "/addItems">] ItemEntry
    | [<EndPoint "/compare">] CompareItems
    | [<EndPoint "/result">] Result
    | [<EndPoint "/privacy">] PrivacyPolicy

  type Id = int
  type Votes = int
  type Value = string
  type Item = {
    Id: Id
    Votes: Votes
    Value: Value
  }
  type Pair = {
    A: Item
    B: Item
  }
  type Disabled = {
    Disabled: bool
  }

  [<NamedUnionCases "type">]
  type Model = { 
    EndPoint: EndPoint

    // State for entering new items
    PendingItemValue: string
    Items: Item list
    NextId: int

    // State for voting phase
    PendingCombinations: Pair list
  }

  let initialModel = {
      EndPoint = EndPoint.Home

      // Items
      PendingItemValue = ""
      Items = []
      NextId = 0

      // Voting state
      PendingCombinations = []
    }

  type WinnerOfPair = A | B

  type Message = 
    | Goto of EndPoint
    | AddNewItem
    | RemoveItem of Id
    | UpdatePendingItemValue of Value
    | StartVotingPhase
    | RecordVote of WinnerOfPair
    | Restart

  let Update (msg: Message) (model: Model) : Action<Message, Model> =
    match msg with
    | Goto place -> SetModel {model with EndPoint = place}

    // Update states for managing items
    | AddNewItem ->
        let newItem = {
          Id = model.NextId
          Votes = 0
          Value = model.PendingItemValue
        }
        SetModel { 
            model with 
              PendingItemValue = ""
              Items = newItem :: model.Items
              NextId = model.NextId + 1
          }
    | UpdatePendingItemValue value ->
        SetModel { model with PendingItemValue = value }
    | RemoveItem idToRemove ->
        let prunedList = List.filter (fun item -> item.Id <> idToRemove) model.Items
        SetModel {model with Items = prunedList}
    | StartVotingPhase ->
        let permutations = 
          model.Items
          |> createPermutations 
          |> List.map(fun (a, b) -> { A=a; B=b })
        let updateModel = { 
          model with 
            PendingCombinations = permutations
            EndPoint = EndPoint.CompareItems
        }
        SetModel updateModel
    | RecordVote which -> 
        let currentPair = List.head(model.PendingCombinations)
        let winningItem = match which with
                          | A -> currentPair.A
                          | B -> currentPair.B
        Console.Log(sprintf "Recording option %A from pair %A" which currentPair)
        let mapFun = fun item ->
          if item.Id = winningItem.Id then
            { item with Votes = item.Votes + 1 }
          else
            item
        let updatedModel = {  model with Items = List.map mapFun model.Items }
        // This is a nasty hack... If I set PendingCombinations to [] as a result of tail
        // which is what I really should be doing, then the render 
        if List.length(model.PendingCombinations) = 1 then
          SetModel { updatedModel with EndPoint = EndPoint.Result }
        else
          SetModel { updatedModel with PendingCombinations = List.tail(model.PendingCombinations) }
    | Restart ->
        SetModel { model with Items = []; PendingCombinations = []; EndPoint = EndPoint.ItemEntry }

  module Pages =
    let MainPage = Page.Single(render = fun (dispatch: Dispatch<Message>) _ -> 
        Templates
          .MainPage()
          .ClickNext(fun _ -> dispatch (Goto EndPoint.ItemEntry))
          .Doc()
      )

    let ItemEntry = Page.Single(render = fun (dispatch: Dispatch<Message>) (model: View<Model>) -> 
        Templates
          .ItemEntryPage()
          .ClickNext(fun _ -> dispatch StartVotingPhase)
          .ClickNextAttr(
            let whetherDisabled = View.Map (fun model -> { Disabled = List.length(model.Items) < 2 }) model
            Attr.Prop "disabled" whetherDisabled.V.Disabled
          )
          .PendingNewItemVar(
            V(model.V.PendingItemValue),
            UpdatePendingItemValue >> dispatch 
          )
          .AddItem(fun _ -> dispatch AddNewItem)
          .AddItemAttr(
            let whetherDisabled = View.Map (fun model -> { Disabled = model.PendingItemValue.Trim() |> String.length = 0 }) model
            Attr.Prop "disabled" whetherDisabled.V.Disabled
          )
          .PendingNewItemKeyDown(fun e ->
            if e.Event.Key = "Enter" then
              dispatch AddNewItem
              e.Event.PreventDefault()
          )
          .Content(
            Templates
              .ItemsTemplate()
              .ItemsHeaderAttr(
                let shouldBeDisabled = View.Map (fun model -> { Disabled = List.length(model.Items) = 0}) model
                Attr.Prop "hidden" shouldBeDisabled.V.Disabled
              )
              .ItemsHole(
                V(model.V.Items).DocSeqCached(fun item ->
                  Templates
                    .ItemTemplate()
                    .RemoveItem(fun e -> 
                      dispatch (RemoveItem item.Id)
                      e.Event.PreventDefault()
                    )
                    .Value(item.Value)
                    .Doc()
                )
              )
              .Doc()
          )
          .Doc()
      )

    let CompareItems = Page.Single(render = fun (dispatch: Dispatch<Message>) (model: View<Model>) -> 
      let nextPair = View.Map (fun model -> List.head model.PendingCombinations) model
      Templates
        .ComparisonTemplate()
        .ValueA(nextPair.V.A.Value)
        .ValueB(nextPair.V.B.Value)
        .AWins(fun e ->
          dispatch (RecordVote A)
          e.Event.PreventDefault()
        )
        .BWins(fun e ->
          dispatch (RecordVote B)
          e.Event.PreventDefault()
        )
        .Doc()
    )

    let Result = Page.Single(render = fun (dispatch: Dispatch<Message>) (model: View<Model>) -> 
      Templates
        .ResultsTemplate()
        .Results(
          let sortedItems = 
            View.Map 
              (fun model -> 
                model.Items
                |> List.sortBy (fun (item: Item) -> item.Votes) 
                |> List.rev) 
              model
          sortedItems.DocSeqCached(fun item ->
            Templates
              .ResultsItemTemplate()
              .Value(item.Value)
              .VoteCount(sprintf "%i" item.Votes)
              .Doc()
          )
        )
        .StartOver(fun _ -> dispatch Restart)
        .Doc()
    )

    let PrivacyPolicy = Page.Single(render = fun _ _ ->
      Templates
        .PrivacyPolicyTemplate()
        .Doc()
    )

  let Render (model: Model) = 
    match model.EndPoint with
    | EndPoint.Home -> Pages.MainPage ()
    | EndPoint.ItemEntry -> Pages.ItemEntry ()
    | EndPoint.CompareItems -> Pages.CompareItems ()
    | EndPoint.Result -> Pages.Result ()
    | EndPoint.PrivacyPolicy -> Pages.PrivacyPolicy ()

  let Navigate (page: EndPoint) (model: Model) : Model =
    { model with EndPoint = page }

  let router = Router.Infer<EndPoint>()

  [<SPAEntryPoint>]
  let Main () =
    App.CreatePaged initialModel Update Render
    |> App.WithCustomRouting router (fun s -> s.EndPoint) Navigate
    |> App.WithLocalStorage "PairRankStore"
    |> App.Run
    |> Doc.RunById "main"
