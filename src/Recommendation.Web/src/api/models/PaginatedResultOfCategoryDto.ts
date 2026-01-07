/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CategoryDto } from './CategoryDto';
export type PaginatedResultOfCategoryDto = {
    items: Array<CategoryDto>;
    totalCount: number | string;
    page: number | string;
    pageSize: number | string;
    totalPages: number | string;
    hasMore: boolean;
};

