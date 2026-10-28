1. **Remove Pagination Component**: Remove `<Pagination PageNumber="@(page)" Total="@total" Busy="@Busy" PageChanged="SetPage" />` from `src/VeganAdda.UI/Pages/Discover.razor`.
2. **Add Infinite Scroll Trigger**: Add a `div` element at the end of the `people-grid` or below it, which will act as the trigger for the intersection observer. E.g.
```html
@if (people.Count > 0 && people.Count < total)
{
    <div @ref="loadMoreTrigger" class="loading-more">
        @if (Busy) { <span>Loading more...</span> }
    </div>
}
```
3. **Implement Intersection Observer in JS**: Modify `src/VeganAdda.UI/wwwroot/discovery.js` to include the `observeScroll` and `unobserveScroll` methods (I've already added them in a previous bash run, but I'll make sure they are correct and clean).
4. **Update C# code in `Discover.razor`**:
   - Change `people = result.Items;` to `if (page == 1) people = result.Items; else people.AddRange(result.Items);`.
   - Ensure that `Search()` resets `page = 1` and clears the `people` list before fetching (it already sets `page = 1`).
   - Add a `[JSInvokable]` method `LoadNextPage()` that increments `page` and calls `Run(Refresh)`.
   - In `OnAfterRenderAsync`, if the `loadMoreTrigger` element reference is set and we're not busy, observe it using the JS module. Need to manage the JS module reference and the `.NET` object reference (`DotNetObjectReference.Create(this)`).
   - In `Dispose()`, disconnect the observer and dispose the `DotNetObjectReference`.
