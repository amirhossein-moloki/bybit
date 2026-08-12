export interface ApiErrorBody {
  status: string;
  error: {
    code: string;
    message: string;
    correlationId?: string;
  };
}

export interface ApiSuccess<T> {
  status: "success";
  data: T;
}

export interface PagedResult<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  items: T[];
}
