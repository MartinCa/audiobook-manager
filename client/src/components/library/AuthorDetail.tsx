import { useState } from "react";
import { Link, useNavigate, useRouter } from "@tanstack/react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ArrowLeft, Users, BookMarked, BookOpen, ChevronRight, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { PAGE_SIZE } from "@/constants/paging";
import { BookListRow } from "./BookListRow";
import { BookBulkActionBar } from "./BookBulkActionBar";
import { browseApi } from "@/services/api";
import { useClampedPage } from "@/hooks/useClampedPage";
import { useBookSelection } from "@/hooks/useBookSelection";
import { Route } from "@/routes/library/authors/$authorId";

export function AuthorDetail() {
  const { authorId } = Route.useParams();
  const navigate = useNavigate();
  const router = useRouter();
  const id = Number(authorId);

  // Each section pages server-side; one has its own page state so paging series books doesn't
  // move the standalone list. The unpaged version sent an author's entire catalogue at once.
  const [seriesPage, setSeriesPage] = useState(0);
  const [standalonePage, setStandalonePage] = useState(0);
  const selection = useBookSelection();

  // Navigating between authors must not carry a previous author's page cursor along. Adjusted
  // during render (React's documented pattern) rather than in an effect: the query key below
  // already changes with the author, so this only resets the local paging state when it does.
  // The book selection resets for the same reason - a different author owns a different roster.
  const [prevId, setPrevId] = useState(id);
  if (prevId !== id) {
    setPrevId(id);
    setSeriesPage(0);
    setStandalonePage(0);
    selection.clear();
  }

  // One combined detail query instead of two: the endpoint already computes both sections on
  // every call and accepts both sections' cursors, so separate queries made every section
  // change issue an extra backend call whose other section (computed with default paging) was
  // thrown away. keepPreviousData keeps both sections rendered while one of them pages.
  const detailQuery = useQuery({
    queryKey: ["author", id, seriesPage, standalonePage],
    queryFn: () =>
      browseApi.getAuthorDetail(id, {
        seriesLimit: PAGE_SIZE,
        seriesOffset: seriesPage * PAGE_SIZE,
        standaloneLimit: PAGE_SIZE,
        standaloneOffset: standalonePage * PAGE_SIZE,
      }),
    enabled: Boolean(id),
    placeholderData: keepPreviousData,
  });

  const author = detailQuery.data?.author;
  const seriesSection = detailQuery.data?.series ?? { items: [], total: 0 };
  const standaloneSection = detailQuery.data?.standaloneBooks ?? { items: [], total: 0 };

  const seriesPageCount = Math.max(1, Math.ceil(seriesSection.total / PAGE_SIZE));
  const standalonePageCount = Math.max(1, Math.ceil(standaloneSection.total / PAGE_SIZE));
  // Clamped so the fetched and the displayed page can never disagree.
  const currentSeriesPage = Math.min(seriesPage, seriesPageCount - 1);
  const currentStandalonePage = Math.min(standalonePage, standalonePageCount - 1);

  // And the raw page states are pulled back into range once a response shows a section shrank
  // under them (e.g. books moved between sections from another tab), so the next fetch - not
  // just the display - lands on a valid page.
  useClampedPage(seriesPage, seriesPageCount, setSeriesPage);
  useClampedPage(standalonePage, standalonePageCount, setStandalonePage);

  const handleBack = () => {
    if (router.history.canGoBack()) {
      router.history.back();
    } else {
      void navigate({ to: "/library/authors" });
    }
  };

  if (!author && detailQuery.isLoading) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
        <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
        <p className="text-sm">Loading author details...</p>
      </div>
    );
  }

  if (!author) {
    return (
      <div className="space-y-4 py-12 text-center">
        <h2 className="text-xl font-bold">Author not found</h2>
        <Button render={<Link to="/library/authors" />}>Back to Authors</Button>
      </div>
    );
  }

  const series = seriesSection.items;
  const standaloneBooks = standaloneSection.items;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" onClick={handleBack}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Authors
        </Button>
      </div>

      <div className="border-border border-b pb-4">
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Users className="text-primary h-6 w-6" />
          {author.name}
        </h1>
        <p className="text-muted-foreground text-sm">
          {author.bookCount} {author.bookCount === 1 ? "audiobook" : "audiobooks"} in library
        </p>
      </div>

      {seriesSection.total > 0 && (
        <div className="space-y-3">
          <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
            <BookMarked className="text-primary h-5 w-5" />
            Series ({seriesSection.total})
          </h2>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {series.map((s) => (
              <Link
                key={s.seriesName}
                to="/library/series/$seriesName"
                params={{ seriesName: s.seriesName }}
                search={{ authorId: author.id }}
                className="focus-visible:ring-ring block rounded-lg focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
              >
                <Card className="hover:bg-muted/50 cursor-pointer transition-colors">
                  <CardHeader className="p-4 pb-2">
                    <CardTitle className="text-base font-semibold">{s.seriesName}</CardTitle>
                  </CardHeader>
                  <CardContent className="text-muted-foreground flex items-center justify-between p-4 pt-0 text-xs">
                    <span>
                      {s.bookCount} {s.bookCount === 1 ? "book" : "books"}
                    </span>
                    <ChevronRight className="h-4 w-4" />
                  </CardContent>
                </Card>
              </Link>
            ))}
          </div>
          {seriesPageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-muted-foreground text-xs">
                Showing {currentSeriesPage * PAGE_SIZE + 1}–
                {Math.min((currentSeriesPage + 1) * PAGE_SIZE, seriesSection.total)} of{" "}
                {seriesSection.total}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentSeriesPage === 0}
                  onClick={() => setSeriesPage(currentSeriesPage - 1)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentSeriesPage >= seriesPageCount - 1}
                  onClick={() => setSeriesPage(currentSeriesPage + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      {standaloneSection.total > 0 && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
              <BookOpen className="text-primary h-5 w-5" />
              Standalone Audiobooks ({standaloneSection.total})
            </h2>
            <Checkbox
              id="select-standalone-page"
              disabled={standaloneBooks.length === 0}
              checked={standaloneBooks.length > 0 && selection.pageAllSelected(standaloneBooks)}
              indeterminate={
                standaloneBooks.length > 0 && selection.pageSomeSelected(standaloneBooks)
              }
              onCheckedChange={(checked) => {
                if (checked) {
                  selection.selectPage(standaloneBooks);
                } else {
                  selection.deselectPage(standaloneBooks);
                }
              }}
            />
            <label
              htmlFor="select-standalone-page"
              className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
            >
              Select page
            </label>
          </div>
          <BookBulkActionBar selection={selection} />
          <div className="space-y-2">
            {standaloneBooks.map((book) => (
              <BookListRow
                key={book.id}
                book={book}
                selectable
                selected={selection.isSelected(book.id)}
                onSelectedChange={() => selection.toggle(book)}
              />
            ))}
          </div>
          {standalonePageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
              <span className="text-muted-foreground text-xs">
                Showing {currentStandalonePage * PAGE_SIZE + 1}–
                {Math.min((currentStandalonePage + 1) * PAGE_SIZE, standaloneSection.total)} of{" "}
                {standaloneSection.total}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentStandalonePage === 0}
                  onClick={() => setStandalonePage(currentStandalonePage - 1)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentStandalonePage >= standalonePageCount - 1}
                  onClick={() => setStandalonePage(currentStandalonePage + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default AuthorDetail;
