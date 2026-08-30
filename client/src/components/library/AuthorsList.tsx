import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Users, Search, ChevronRight, Loader2, BookOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { browseApi } from "@/services/api";
import { foldAccents } from "@/helpers/similarValueMatcher";

export function AuthorsList() {
  const navigate = useNavigate();
  const [filter, setFilter] = useState("");

  const { data: authors = [], isLoading: loading } = useQuery({
    queryKey: ["authors"],
    queryFn: () => browseApi.getAuthors(),
  });

  const filteredAuthors = authors.filter((a) => {
    if (!filter.trim()) return true;
    const q = foldAccents(filter.trim().toLowerCase());
    return foldAccents(a.name.toLowerCase()).includes(q);
  });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Users className="text-primary h-6 w-6" />
          Authors ({authors.length})
        </h1>
        <p className="text-muted-foreground text-sm">Browse books and series grouped by author.</p>
      </div>

      <div className="relative max-w-md">
        <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
        <Input
          placeholder="Filter authors..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="pl-9"
        />
      </div>

      {loading ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading authors...</p>
        </div>
      ) : filteredAuthors.length === 0 ? (
        <Card className="p-12 text-center">
          <Users className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No authors found</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            {filter ? "No authors match your search filter." : "No authors tracked in the library."}
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {filteredAuthors.map((author) => (
            <div
              key={author.id}
              onClick={() => {
                void navigate(`/library/authors/${author.id}`);
              }}
              className="group border-border bg-card hover:bg-muted/50 flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors"
            >
              <div className="flex items-center gap-3">
                <BookOpen className="text-primary h-4 w-4" />
                <div>
                  <div className="text-foreground font-semibold">{author.name}</div>
                  <div className="text-muted-foreground text-xs">
                    {author.bookCount} {author.bookCount === 1 ? "book" : "books"}
                  </div>
                </div>
              </div>

              <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default AuthorsList;
