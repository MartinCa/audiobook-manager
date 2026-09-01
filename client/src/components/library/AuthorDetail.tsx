import { Link, useNavigate, useRouter } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Users, BookMarked, BookOpen, ChevronRight, Loader2, Clock } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { browseApi } from "@/services/api";
import { formatDuration } from "@/helpers/formatHelpers";
import { Route } from "@/routes/library/authors/$authorId";

export function AuthorDetail() {
  const { authorId } = Route.useParams();
  const navigate = useNavigate();
  const router = useRouter();
  const id = Number(authorId);

  const handleBack = () => {
    if (router.history.canGoBack()) {
      router.history.back();
    } else {
      void navigate({ to: "/library/authors" });
    }
  };

  const { data: detail, isLoading: loading } = useQuery({
    queryKey: ["author", id],
    queryFn: () => browseApi.getAuthorDetail(id),
    enabled: Boolean(id),
  });

  if (loading) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
        <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
        <p className="text-sm">Loading author details...</p>
      </div>
    );
  }

  if (!detail) {
    return (
      <div className="space-y-4 py-12 text-center">
        <h2 className="text-xl font-bold">Author not found</h2>
        <Button render={<Link to="/library/authors" />}>Back to Authors</Button>
      </div>
    );
  }

  const { author, series, standaloneBooks } = detail;

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

      {series.length > 0 && (
        <div className="space-y-3">
          <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
            <BookMarked className="text-primary h-5 w-5" />
            Series ({series.length})
          </h2>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {series.map((s) => (
              <Card
                key={s.seriesName}
                onClick={() => {
                  void navigate({
                    to: "/library/series/$seriesName",
                    params: { seriesName: s.seriesName },
                    search: { authorId: author.id },
                  });
                }}
                className="hover:bg-muted/50 cursor-pointer transition-colors"
              >
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
            ))}
          </div>
        </div>
      )}

      {standaloneBooks.length > 0 && (
        <div className="space-y-3">
          <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
            <BookOpen className="text-primary h-5 w-5" />
            Standalone Audiobooks ({standaloneBooks.length})
          </h2>
          <div className="space-y-2">
            {standaloneBooks.map((book) => (
              <div
                key={book.id}
                onClick={() => {
                  void navigate({
                    to: "/library/book/$bookId",
                    params: { bookId: String(book.id) },
                  });
                }}
                className="group border-border bg-card hover:bg-muted/50 flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors"
              >
                <div className="min-w-0">
                  <div className="text-foreground font-semibold">
                    {book.bookName}
                    {book.year && (
                      <span className="text-muted-foreground text-xs font-normal">
                        {" "}
                        ({book.year})
                      </span>
                    )}
                  </div>

                  <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 text-xs">
                    {book.narrators && book.narrators.length > 0 && (
                      <span>Narrated by {book.narrators.join(", ")}</span>
                    )}
                    {book.durationInSeconds != null && (
                      <span className="flex items-center gap-1">
                        <Clock className="h-3 w-3" />
                        {formatDuration(book.durationInSeconds)}
                      </span>
                    )}
                  </div>
                </div>

                <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export default AuthorDetail;
